using System.IO.Compression;
using System.Text;
using CarKit.ReleaseGate;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Xunit;

namespace CarKit.Maui.Build.Tests.ReleaseGate;

// Stage 10: release gate (eng/ReleaseGate; spec: "Documentation and release decisions (Stage 10)", Release gate) on
// fixture packages: nuspec, README, dll in the nupkg, portable PDB from Roslyn (Source Link, /_/ paths) in the snupkg,
// PublicAPI files in the "repo" directory. The repository address of the fixtures is deliberately fictitious.
public class ReleaseGateTests
{
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";
    private const string Repository = "https://github.com/carkit-test/CarKit.Maui";

    [Fact]
    public void Complete_release_passes_every_check()
    {
        using var release = new FixtureRelease();

        var results = release.Run("v1.0.0");

        Assert.All(results, r => Assert.True(r.Passed, $"{r.Name}: {r.Message}"));
        Assert.Equal(["packages", "version", "license", "repository", "readme", "symbols", "sourcelink", "publicapi"], results.Select(r => r.Name));
    }

    [Fact]
    public void Missing_symbol_package_fails()
    {
        using var release = new FixtureRelease { WithoutSymbolPackage = "CarKit.Maui.Media" };

        var result = release.Check("packages", "v1.0.0");

        Assert.False(result.Passed);
        Assert.Contains("CarKit.Maui.Media.1.0.0.snupkg", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_package_fails()
    {
        using var release = new FixtureRelease { Without = "CarKit.Maui.Testing" };

        var result = release.Check("packages", "v1.0.0");

        Assert.False(result.Passed);
        Assert.Contains("CarKit.Maui.Testing", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Wrong_version_fails()
    {
        using var release = new FixtureRelease();

        var result = release.Check("version", "v1.0.1");

        Assert.False(result.Passed);
        Assert.Contains("1.0.1", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_tag_fails_version()
    {
        using var release = new FixtureRelease();

        Assert.False(release.Check("version", null).Passed);
    }

    [Fact]
    public void Prerelease_tag_matches_prerelease_packages()
    {
        using var release = new FixtureRelease { Version = "1.0.0-rc.1" };

        Assert.True(release.Check("version", "v1.0.0-rc.1").Passed);
    }

    [Fact]
    public void Missing_license_fails()
    {
        using var release = new FixtureRelease { License = null };

        var result = release.Check("license", "v1.0.0");

        Assert.False(result.Passed);
        Assert.Contains("PackageLicenseExpression", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void License_file_missing_from_package_fails()
    {
        using var release = new FixtureRelease { License = "file:LICENSE.txt", LicenseFileInPackage = false };

        Assert.False(release.Check("license", "v1.0.0").Passed);
    }

    [Fact]
    public void License_file_in_package_passes()
    {
        using var release = new FixtureRelease { License = "file:LICENSE.txt" };

        Assert.True(release.Check("license", "v1.0.0").Passed);
    }

    [Fact]
    public void Repository_without_commit_fails()
    {
        using var release = new FixtureRelease { RepositoryCommit = null };

        var result = release.Check("repository", "v1.0.0");

        Assert.False(result.Passed);
        Assert.Contains("commit", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_readme_fails()
    {
        using var release = new FixtureRelease { WithReadme = false };

        Assert.False(release.Check("readme", "v1.0.0").Passed);
    }

    [Fact]
    public void Dll_without_pdb_fails_symbols()
    {
        using var release = new FixtureRelease { WithoutPdb = "CarKit.Maui.Navigation" };

        var result = release.Check("symbols", "v1.0.0");

        Assert.False(result.Passed);
        Assert.Contains("lib/net10.0/CarKit.Maui.Navigation.pdb", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Pdb_without_source_link_fails()
    {
        using var release = new FixtureRelease { SourceLink = null };

        var result = release.Check("sourcelink", "v1.0.0");

        Assert.False(result.Passed);
        Assert.Contains("no Source Link", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Source_link_to_other_commit_fails()
    {
        using var release = new FixtureRelease { SourceLink = SourceLinkJson("fedcba9876543210fedcba9876543210fedcba98") };

        Assert.False(release.Check("sourcelink", "v1.0.0").Passed);
    }

    [Fact]
    public void Source_link_to_other_repository_fails()
    {
        using var release = new FixtureRelease
        {
            SourceLink = $$$"""{"documents":{"/_/*":"https://raw.githubusercontent.com/somebody-else/Fork/{{{Commit}}}/*"}}""",
        };

        var result = release.Check("sourcelink", "v1.0.0");

        Assert.False(result.Passed);
        Assert.Contains("somebody-else/Fork", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Pdb_with_local_paths_fails_sourcelink()
    {
        using var release = new FixtureRelease { DocumentPath = "/Users/dev/CarKit.Maui/src/Probe.cs" };

        var result = release.Check("sourcelink", "v1.0.0");

        Assert.False(result.Passed);
        Assert.Contains("/Users/dev/CarKit.Maui/src/Probe.cs", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Unshipped_api_fails_publicapi()
    {
        using var release = new FixtureRelease { UnshippedApi = "#nullable enable\nCarKit.Maui.NewType\n" };

        var result = release.Check("publicapi", "v1.0.0");

        Assert.False(result.Passed);
        Assert.Contains("CarKit.Maui.NewType", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Command_line_reports_each_check_and_fails_with_exit_code_1()
    {
        using var release = new FixtureRelease { License = null };
        var output = new StringWriter();

        var exitCode = ReleaseGateProgram.Run(["--packages", release.Written().Packages, "--tag", "v1.0.0", "--repo", release.Root], output);

        Assert.Equal(1, exitCode);
        Assert.Contains("FAIL license:", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("PASS packages", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Command_line_passes_with_exit_code_0()
    {
        using var release = new FixtureRelease();
        var output = new StringWriter();

        Assert.Equal(0, ReleaseGateProgram.Run(["--packages", release.Written().Packages, "--tag", "v1.0.0", "--repo", release.Root], output));
        Assert.Contains("RELEASE-GATE: PASS 8 FAIL 0", output.ToString(), StringComparison.Ordinal);
    }

    private static string SourceLinkJson(string commit) =>
        $$$"""{"documents":{"/_/*":"https://raw.githubusercontent.com/carkit-test/CarKit.Maui/{{{commit}}}/*"}}""";

    private sealed class FixtureRelease : IDisposable
    {
        private bool _written;

        public FixtureRelease()
        {
            Root = Path.Combine(AppContext.BaseDirectory, "release-gate", Guid.NewGuid().ToString("N"));
            Packages = Path.Combine(Root, "artifacts", "packages");
        }

        public string Root { get; }

        public string Packages { get; }

        public string Version { get; init; } = "1.0.0";

        public string? License { get; init; } = "expression:LicenseRef-Fixture";

        public bool LicenseFileInPackage { get; init; } = true;

        public string? RepositoryCommit { get; init; } = Commit;

        public bool WithReadme { get; init; } = true;

        public string? Without { get; init; }

        public string? WithoutSymbolPackage { get; init; }

        public string? WithoutPdb { get; init; }

        public string? SourceLink { get; init; } = SourceLinkJson(Commit);

        public string DocumentPath { get; init; } = "/_/src/Probe.cs";

        public string UnshippedApi { get; init; } = "#nullable enable\n";

        public IReadOnlyList<ReleaseCheckResult> Run(string? tag)
        {
            Write();
            return ReleaseChecks.Run(Packages, tag, Root);
        }

        public FixtureRelease Written()
        {
            Write();
            return this;
        }

        public ReleaseCheckResult Check(string name, string? tag) => Run(tag).Single(r => r.Name == name);

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }

        private void Write()
        {
            if (_written)
            {
                return;
            }

            _written = true;
            Directory.CreateDirectory(Packages);
            var api = Path.Combine(Root, "src", "CarKit.Maui.Core", "PublicAPI", "net");
            Directory.CreateDirectory(api);
            File.WriteAllText(Path.Combine(api, "PublicAPI.Shipped.txt"), "#nullable enable\nCarKit.Maui.CarListTemplate\n");
            File.WriteAllText(Path.Combine(api, "PublicAPI.Unshipped.txt"), UnshippedApi);

            var pdb = Pdb();
            foreach (var id in ReleaseChecks.PackageIds.Where(id => id != Without))
            {
                WriteZip(Path.Combine(Packages, $"{id}.{Version}.nupkg"), new()
                {
                    [$"{id}.nuspec"] = Encoding.UTF8.GetBytes(Nuspec(id)),
                    [$"lib/net10.0/{id}.dll"] = [0x4d, 0x5a],
                    ["README.md"] = WithReadme ? "# Readme"u8.ToArray() : null,
                    ["LICENSE.txt"] = LicenseFileInPackage && License?.StartsWith("file:", StringComparison.Ordinal) == true ? "Fixture"u8.ToArray() : null,
                });
                if (id != WithoutSymbolPackage)
                {
                    WriteZip(Path.Combine(Packages, $"{id}.{Version}.snupkg"), new()
                    {
                        [$"{id}.nuspec"] = Encoding.UTF8.GetBytes(Nuspec(id)),
                        [$"lib/net10.0/{id}.pdb"] = id == WithoutPdb ? null : pdb,
                    });
                }
            }
        }

        private string Nuspec(string id)
        {
            var license = License?.Split(':', 2) is [var type, var value] ? $"<license type=\"{type}\">{value}</license>" : string.Empty;
            var commit = RepositoryCommit is null ? string.Empty : $" commit=\"{RepositoryCommit}\"";
            var readme = WithReadme ? "<readme>README.md</readme>" : string.Empty;
            return $"""
                <?xml version="1.0" encoding="utf-8"?>
                <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
                  <metadata>
                    <id>{id}</id>
                    <version>{Version}</version>
                    <authors>Fixture</authors>
                    {license}
                    {readme}
                    <description>Fixture</description>
                    <repository type="git" url="{Repository}"{commit} />
                  </metadata>
                </package>
                """;
        }

        private byte[] Pdb()
        {
            var tree = CSharpSyntaxTree.ParseText("public static class Probe { public static int Answer() => 42; }", path: DocumentPath, encoding: Encoding.UTF8);
            var compilation = CSharpCompilation.Create(
                "Probe",
                [tree],
                [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            using var pe = new MemoryStream();
            using var pdb = new MemoryStream();
            using var sourceLink = SourceLink is null ? null : new MemoryStream(Encoding.UTF8.GetBytes(SourceLink));
            var result = compilation.Emit(pe, pdb, options: new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb), sourceLinkStream: sourceLink);
            Assert.True(result.Success, string.Join('\n', result.Diagnostics));
            return pdb.ToArray();
        }

        private static void WriteZip(string path, Dictionary<string, byte[]?> entries)
        {
            using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
            foreach (var (name, content) in entries)
            {
                if (content is not null)
                {
                    using var stream = zip.CreateEntry(name).Open();
                    stream.Write(content);
                }
            }
        }
    }
}
