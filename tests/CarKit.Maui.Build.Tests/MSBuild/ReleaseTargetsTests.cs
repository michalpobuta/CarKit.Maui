using System.Diagnostics;
using System.IO.Compression;
using System.Xml.Linq;
using Xunit;

namespace CarKit.Maui.Build.Tests.MSBuild;

// Stage 10: version, release mode and repository in the pack (eng/CarKit.Release.props/.targets; spec: "Documentation
// and release decisions (Stage 10)"). The probe is an SDK project in the tests' bin/: the repo's Directory.Build.props
// (version, release mode) works as in src/, and the release targets are imported explicitly. The tests disable git
// queries wherever the result depends on the clone.
public class ReleaseTargetsTests
{
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";
    private const string TestRepository = "https://github.com/carkit-test/CarKit.Maui";

    [Fact]
    public void Dev_pack_has_dev_suffix()
    {
        var result = Pack(new() { ["EnableSourceControlManagerQueries"] = "false" });

        Assert.Equal(0, result.ExitCode);
        Assert.Equal($"{VersionPrefix}-dev", result.Metadata("version"));
    }

    [Fact]
    public void Dev_pack_without_commit_reports_missing_repository()
    {
        var result = Pack(new() { ["EnableSourceControlManagerQueries"] = "false" });

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("CarKit: the package Probe has no repository URL or commit", result.Log, StringComparison.Ordinal);
    }

    [Fact]
    public void Release_pack_without_license_fails_CARKITREL001()
    {
        var result = Pack(new() { ["CarKitRelease"] = "true", ["CarKitSourceRevisionId"] = Commit, ["CarKitRepositoryUrl"] = TestRepository });

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("error CARKITREL001", result.Log, StringComparison.Ordinal);
        Assert.DoesNotContain("error CARKITREL002", result.Log, StringComparison.Ordinal);
        Assert.Null(result.Package);
    }

    // All gaps at once and no package: a release pack must not leave a .nupkg next to the errors
    [Fact]
    public void Release_pack_reports_every_problem_and_creates_no_package()
    {
        var result = Pack(new() { ["CarKitReleaseTag"] = "v0.0.1", ["EnableSourceControlManagerQueries"] = "false" });

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("error CARKITREL001", result.Log, StringComparison.Ordinal);
        Assert.Contains("error CARKITREL002", result.Log, StringComparison.Ordinal);
        Assert.Contains("error CARKITREL003", result.Log, StringComparison.Ordinal);
        Assert.Null(result.Package);
    }

    // A license entered by the owner in Directory.Build.props must not change the probe results (step 1 of the
    // release checklist)
    [Fact]
    public void Probes_ignore_a_license_of_the_repository()
    {
        var result = Pack(
            new() { ["CarKitRelease"] = "true", ["CarKitSourceRevisionId"] = Commit, ["CarKitRepositoryUrl"] = TestRepository },
            new() { ["PackageLicenseFile"] = "LICENSE-TEST.txt" });

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("error CARKITREL001", result.Log, StringComparison.Ordinal);
    }

    [Fact]
    public void Release_pack_without_commit_fails_CARKITREL002()
    {
        var result = Pack(new() { ["CarKitRelease"] = "true", ["EnableSourceControlManagerQueries"] = "false", ["PackageLicenseFile"] = "LICENSE-TEST.txt" });

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("error CARKITREL002", result.Log, StringComparison.Ordinal);
        Assert.DoesNotContain("error CARKITREL001", result.Log, StringComparison.Ordinal);
        Assert.Null(result.Package);
    }

    [Fact]
    public void Release_tag_with_suffix_sets_the_version()
    {
        var result = Pack(new()
        {
            ["CarKitReleaseTag"] = $"v{VersionPrefix}-rc.1",
            ["CarKitSourceRevisionId"] = Commit,
            ["CarKitRepositoryUrl"] = TestRepository,
            ["PackageLicenseFile"] = "LICENSE-TEST.txt",
        });

        Assert.Equal(0, result.ExitCode);
        Assert.Equal($"{VersionPrefix}-rc.1", result.Metadata("version"));
    }

    [Fact]
    public void Release_tag_without_suffix_has_no_suffix()
    {
        var result = Pack(new()
        {
            ["CarKitReleaseTag"] = $"v{VersionPrefix}",
            ["CarKitSourceRevisionId"] = Commit,
            ["CarKitRepositoryUrl"] = TestRepository,
            ["PackageLicenseFile"] = "LICENSE-TEST.txt",
        });

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(VersionPrefix, result.Metadata("version"));
    }

    [Fact]
    public void Release_tag_with_other_prefix_fails_CARKITREL003()
    {
        var result = Pack(new()
        {
            ["CarKitReleaseTag"] = "v0.0.1",
            ["CarKitSourceRevisionId"] = Commit,
            ["CarKitRepositoryUrl"] = TestRepository,
            ["PackageLicenseFile"] = "LICENSE-TEST.txt",
        });

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("error CARKITREL003", result.Log, StringComparison.Ordinal);
        Assert.Null(result.Package);
    }

    [Fact]
    public void Github_tag_build_is_a_release()
    {
        var result = Pack(
            new() { ["CarKitSourceRevisionId"] = Commit, ["CarKitRepositoryUrl"] = TestRepository },
            new() { ["GITHUB_REF_TYPE"] = "tag", ["GITHUB_REF_NAME"] = $"v{VersionPrefix}" });

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("error CARKITREL001", result.Log, StringComparison.Ordinal);
        Assert.DoesNotContain("error CARKITREL003", result.Log, StringComparison.Ordinal);
        Assert.Null(result.Package);
    }

    [Fact]
    public void Github_branch_build_is_not_a_release()
    {
        var result = Pack(
            new() { ["EnableSourceControlManagerQueries"] = "false" },
            new() { ["GITHUB_REF_TYPE"] = "branch", ["GITHUB_REF_NAME"] = "main" });

        Assert.Equal(0, result.ExitCode);
        Assert.Equal($"{VersionPrefix}-dev", result.Metadata("version"));
    }

    [Fact]
    public void Source_revision_without_git_adds_repository_and_project_url()
    {
        var result = Pack(new() { ["CarKitSourceRevisionId"] = Commit, ["CarKitRepositoryUrl"] = TestRepository + ".git" });

        Assert.Equal(0, result.ExitCode);
        var repository = result.Nuspec.Descendants().Single(e => e.Name.LocalName == "repository");
        Assert.Equal((TestRepository + ".git", Commit), ((string?)repository.Attribute("url"), (string?)repository.Attribute("commit")));
        Assert.Equal(TestRepository, result.Metadata("projectUrl"));
        Assert.DoesNotContain("has no repository URL or commit", result.Log, StringComparison.Ordinal);
    }

    private static PackResult Pack(Dictionary<string, string> properties, Dictionary<string, string>? environment = null)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "release-probes", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var targets = Path.GetFullPath(Path.Combine(RepositoryRoot, "eng", "CarKit.Release.targets"));
        File.WriteAllText(Path.Combine(directory, "Probe.csproj"), $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <PackageId>Probe</PackageId>
                <IsPackable>true</IsPackable>
                <Description>Probe</Description>
              </PropertyGroup>
              <ItemGroup>
                <None Include="LICENSE-TEST.txt" Pack="true" PackagePath="" />
              </ItemGroup>
              <Import Project="{targets}" />
            </Project>
            """);
        File.WriteAllText(Path.Combine(directory, "LICENSE-TEST.txt"), "Test fixture of the CarKit release targets. Not a license.");

        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = RepositoryRoot,
        };
        foreach (var key in start.Environment.Keys.Where(k => k.StartsWith("MSBuild", StringComparison.OrdinalIgnoreCase) || k.StartsWith("GITHUB_", StringComparison.Ordinal)).ToList())
        {
            start.Environment.Remove(key);
        }

        foreach (var (name, value) in environment ?? [])
        {
            start.Environment[name] = value;
        }

        start.ArgumentList.Add("pack");
        start.ArgumentList.Add(Path.Combine(directory, "Probe.csproj"));
        start.ArgumentList.Add("-nologo");
        start.ArgumentList.Add("-nodeReuse:false");
        start.ArgumentList.Add("-o");
        start.ArgumentList.Add(Path.Combine(directory, "out"));
        // Global properties win over Directory.Build.props: the repo license does not reach the probe unless the test
        // passes it
        var global = new Dictionary<string, string> { ["PackageLicenseExpression"] = string.Empty, ["PackageLicenseFile"] = string.Empty };
        foreach (var (name, value) in properties)
        {
            global[name] = value;
        }

        foreach (var (name, value) in global)
        {
            start.ArgumentList.Add($"-p:{name}={value}");
        }

        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(TimeSpan.FromMinutes(3)))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("dotnet pack did not finish.");
        }

        var package = Directory.Exists(Path.Combine(directory, "out")) ? Directory.GetFiles(Path.Combine(directory, "out"), "*.nupkg").SingleOrDefault() : null;
        XDocument? nuspec = null;
        if (package is not null)
        {
            using var zip = ZipFile.OpenRead(package);
            using var stream = zip.Entries.Single(e => e.FullName.EndsWith(".nuspec", StringComparison.Ordinal)).Open();
            nuspec = XDocument.Load(stream);
        }

        return new PackResult(process.ExitCode, output.Result + error.Result, nuspec);
    }

    private static string RepositoryRoot { get; } = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(typeof(ReleaseTargetsTests).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false)
        .Cast<System.Reflection.AssemblyMetadataAttribute>().Single(a => a.Key == "CarKitTargets").Value!)!, "..", "..", ".."));

    private static string VersionPrefix { get; } =
        XDocument.Load(Path.Combine(RepositoryRoot, "Directory.Build.props")).Descendants("VersionPrefix").Single().Value;

    private sealed record PackResult(int ExitCode, string Log, XDocument? Package)
    {
        public XDocument Nuspec => Package ?? throw new InvalidOperationException("No package was created:\n" + Log);

        // The nuspec namespace depends on the features used (schema version), so match by local name
        public string Metadata(string name) => (string?)Nuspec.Descendants().SingleOrDefault(e => e.Name.LocalName == name) ?? string.Empty;
    }
}
