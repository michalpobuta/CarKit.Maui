using System.Globalization;
using System.IO.Compression;
using System.Reflection.Metadata;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace CarKit.ReleaseGate;

internal sealed record ReleaseCheckResult(string Name, bool Passed, string Message);

// Checks of finished packages before publishing (spec: "Documentation and release decisions (Stage 10)", Release gate). Each check
// gives one result with a list of all gaps, so that one run shows the full picture.
internal static partial class ReleaseChecks
{
    public static readonly IReadOnlyList<string> PackageIds =
        ["CarKit.Maui.Core", "CarKit.Maui", "CarKit.Maui.Media", "CarKit.Maui.Communication", "CarKit.Maui.Navigation", "CarKit.Maui.Testing"];

    // Source Link in a portable PDB: CustomDebugInformation with this GUID, value = JSON {"documents": {...}}
    private static readonly Guid _sourceLinkKind = new("CC110556-A091-4D38-9FEC-25AB9A351A6A");

    public static IReadOnlyList<ReleaseCheckResult> Run(string packagesDirectory, string? tag, string repositoryRoot)
    {
        var packages = LoadPackages(packagesDirectory);
        return
        [
            CheckPackages(packagesDirectory, packages),
            CheckVersion(packages, tag),
            CheckLicense(packages),
            CheckRepository(packages),
            CheckReadme(packages),
            CheckSymbols(packages),
            CheckSourceLink(packages),
            CheckPublicApi(repositoryRoot),
        ];
    }

    private static List<Package> LoadPackages(string directory)
    {
        var packages = new List<Package>();
        if (!Directory.Exists(directory))
        {
            return packages;
        }

        foreach (var path in Directory.GetFiles(directory, "*.nupkg").Order(StringComparer.Ordinal))
        {
            using var zip = ZipFile.OpenRead(path);
            var nuspecEntry = zip.Entries.FirstOrDefault(e => !e.FullName.Contains('/', StringComparison.Ordinal) && e.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
            if (nuspecEntry is null)
            {
                continue;
            }

            XDocument nuspec;
            using (var stream = nuspecEntry.Open())
            {
                nuspec = XDocument.Load(stream);
            }

            var symbols = Path.ChangeExtension(path, ".snupkg");
            packages.Add(new Package(
                Metadata(nuspec, "id") ?? Path.GetFileNameWithoutExtension(path),
                Metadata(nuspec, "version") ?? string.Empty,
                path,
                File.Exists(symbols) ? symbols : null,
                nuspec,
                [.. zip.Entries.Select(e => e.FullName)]));
        }

        return packages;
    }

    private static ReleaseCheckResult CheckPackages(string directory, List<Package> packages)
    {
        var problems = new List<string>();
        foreach (var id in PackageIds.Where(id => packages.All(p => p.Id != id)))
        {
            problems.Add($"missing {id}.<version>.nupkg");
        }

        foreach (var extra in packages.Where(p => !PackageIds.Contains(p.Id)))
        {
            problems.Add($"unexpected package {Path.GetFileName(extra.Path)}");
        }

        foreach (var package in packages.Where(p => p.SymbolsPath is null))
        {
            problems.Add($"missing {package.Id}.{package.Version}.snupkg");
        }

        var versions = packages.Select(p => p.Version).Distinct(StringComparer.Ordinal).ToList();
        if (versions.Count > 1)
        {
            problems.Add($"packages have different versions: {string.Join(", ", versions)}");
        }

        return Result("packages", problems, $"{packages.Count} .nupkg and {packages.Count(p => p.SymbolsPath is not null)} .snupkg in {directory}");
    }

    private static ReleaseCheckResult CheckVersion(List<Package> packages, string? tag)
    {
        if (string.IsNullOrEmpty(tag) || !tag.StartsWith('v'))
        {
            return new("version", false, "no release tag: pass --tag v<version> (the git tag that triggers the release)");
        }

        var expected = tag[1..];
        var problems = packages.Where(p => p.Version != expected).Select(p => $"{p.Id} has version {p.Version}, tag {tag} needs {expected}").ToList();
        return Result("version", problems, $"all packages have version {expected} (tag {tag})");
    }

    private static ReleaseCheckResult CheckLicense(List<Package> packages)
    {
        var problems = new List<string>();
        foreach (var package in packages)
        {
            var license = package.Nuspec.Descendants().FirstOrDefault(e => e.Name.LocalName == "license");
            var type = (string?)license?.Attribute("type");
            if (license is null || string.IsNullOrWhiteSpace(license.Value))
            {
                problems.Add($"{package.Id} has no license (the owner sets PackageLicenseExpression or PackageLicenseFile)");
            }
            else if (type == "file" && !package.Entries.Contains(license.Value.Trim().Replace('\\', '/')))
            {
                problems.Add($"{package.Id} names license file {license.Value} that is not in the package");
            }
            else if (type is not ("file" or "expression"))
            {
                problems.Add($"{package.Id} has a license of unknown type '{type}'");
            }
        }

        return Result("license", problems, "every package has a license");
    }

    private static ReleaseCheckResult CheckRepository(List<Package> packages)
    {
        var problems = new List<string>();
        foreach (var package in packages)
        {
            var (url, commit) = RepositoryOf(package);
            if (string.IsNullOrEmpty(url))
            {
                problems.Add($"{package.Id} has no repository url");
            }

            if (commit is null || !CommitPattern().IsMatch(commit))
            {
                problems.Add($"{package.Id} has no repository commit (40 hex characters), got '{commit}'");
            }
        }

        return Result("repository", problems, "every package has a repository url and commit");
    }

    private static ReleaseCheckResult CheckReadme(List<Package> packages)
    {
        var problems = new List<string>();
        foreach (var package in packages)
        {
            var readme = Metadata(package.Nuspec, "readme");
            if (string.IsNullOrEmpty(readme))
            {
                problems.Add($"{package.Id} has no readme");
            }
            else if (!package.Entries.Contains(readme.Replace('\\', '/')))
            {
                problems.Add($"{package.Id} names readme {readme} that is not in the package");
            }
        }

        return Result("readme", problems, "every package has a readme");
    }

    private static ReleaseCheckResult CheckSymbols(List<Package> packages)
    {
        var problems = new List<string>();
        var pdbs = 0;
        foreach (var package in packages.Where(p => p.SymbolsPath is not null))
        {
            using var symbols = ZipFile.OpenRead(package.SymbolsPath!);
            foreach (var dll in package.Entries.Where(e => e.StartsWith("lib/", StringComparison.Ordinal) && e.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
            {
                var pdbName = Path.ChangeExtension(dll, ".pdb");
                var entry = symbols.GetEntry(pdbName);
                if (entry is null)
                {
                    problems.Add($"{Path.GetFileName(package.SymbolsPath)} has no {pdbName}");
                    continue;
                }

                if (ReadPdb(entry) is null)
                {
                    problems.Add($"{pdbName} in {Path.GetFileName(package.SymbolsPath)} is not a portable PDB");
                    continue;
                }

                pdbs++;
            }
        }

        return Result("symbols", problems, $"{pdbs} portable PDBs for the libraries");
    }

    private static ReleaseCheckResult CheckSourceLink(List<Package> packages)
    {
        var problems = new List<string>();
        var checkedPdbs = 0;
        foreach (var package in packages.Where(p => p.SymbolsPath is not null))
        {
            var (url, commit) = RepositoryOf(package);
            var repositoryPath = GitHubPath(url);
            using var symbols = ZipFile.OpenRead(package.SymbolsPath!);
            foreach (var entry in symbols.Entries.Where(e => e.FullName.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase)))
            {
                var pdb = ReadPdb(entry);
                if (pdb is null)
                {
                    continue;
                }

                checkedPdbs++;
                var name = $"{package.Id}: {entry.FullName}";
                if (pdb.SourceLink is null)
                {
                    problems.Add($"{name} has no Source Link");
                    continue;
                }

                var targets = SourceLinkTargets(pdb.SourceLink);
                if (targets.Count == 0)
                {
                    problems.Add($"{name} has an unreadable Source Link: {pdb.SourceLink}");
                }
                else if (commit is null || !targets.Any(t => t.Contains(commit, StringComparison.OrdinalIgnoreCase)))
                {
                    problems.Add($"{name} has Source Link {string.Join(", ", targets)} without the repository commit '{commit}'");
                }
                else if (repositoryPath is not null && !targets.Any(t => t.Contains($"/{repositoryPath}/", StringComparison.OrdinalIgnoreCase)))
                {
                    problems.Add($"{name} has Source Link {string.Join(", ", targets)} outside the repository {url}");
                }

                // ContinuousIntegrationBuild maps source paths to /_/ (and /_1/… for other roots): a local path = a build
                // outside CI
                foreach (var document in pdb.Documents.Where(d => !d.StartsWith("/_", StringComparison.Ordinal)).Take(3))
                {
                    problems.Add($"{name} has a local source path {document} (build without ContinuousIntegrationBuild)");
                }
            }
        }

        return Result("sourcelink", problems, $"{checkedPdbs} PDBs with Source Link to the repository commit and deterministic paths");
    }

    private static ReleaseCheckResult CheckPublicApi(string repositoryRoot)
    {
        var problems = new List<string>();
        var source = Path.Combine(repositoryRoot, "src");
        var files = Directory.Exists(source)
            ? Directory.GetFiles(source, "PublicAPI.Unshipped.txt", SearchOption.AllDirectories)
                .Where(f => !Path.GetRelativePath(source, f).Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
                .Order(StringComparer.Ordinal)
                .ToList()
            : [];
        if (files.Count == 0)
        {
            problems.Add($"no PublicAPI.Unshipped.txt under {source}");
        }

        foreach (var file in files)
        {
            var lines = File.ReadAllLines(file).Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            if (lines is not ["#nullable enable"])
            {
                var api = lines.Where(l => l != "#nullable enable").Take(5);
                problems.Add($"{Path.GetRelativePath(repositoryRoot, file)} is not empty (move the API to PublicAPI.Shipped.txt): {string.Join("; ", api)}");
            }
        }

        return Result("publicapi", problems, $"{files.Count} PublicAPI.Unshipped.txt files contain only #nullable enable");
    }

    private static (string? Url, string? Commit) RepositoryOf(Package package)
    {
        var repository = package.Nuspec.Descendants().FirstOrDefault(e => e.Name.LocalName == "repository");
        return ((string?)repository?.Attribute("url"), (string?)repository?.Attribute("commit"));
    }

    // "owner/repo" of a GitHub address (Source Link points to raw.githubusercontent.com/owner/repo/commit); other hosts:
    // commit only
    private static string? GitHubPath(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            ? RepositorySuffix().Replace(uri.AbsolutePath.Trim('/'), string.Empty)
            : null;

    private static Pdb? ReadPdb(ZipArchiveEntry entry)
    {
        using var stream = new MemoryStream();
        using (var source = entry.Open())
        {
            source.CopyTo(stream);
        }

        stream.Position = 0;
        try
        {
            using var provider = MetadataReaderProvider.FromPortablePdbStream(stream);
            var reader = provider.GetMetadataReader();
            string? sourceLink = null;
            foreach (var handle in reader.CustomDebugInformation)
            {
                var information = reader.GetCustomDebugInformation(handle);
                if (reader.GetGuid(information.Kind) == _sourceLinkKind)
                {
                    sourceLink = Encoding.UTF8.GetString(reader.GetBlobBytes(information.Value));
                }
            }

            var documents = reader.Documents.Select(d => reader.GetString(reader.GetDocument(d).Name)).ToList();
            return new Pdb(sourceLink, documents);
        }
        catch (BadImageFormatException)
        {
            return null;
        }
    }

    private static List<string> SourceLinkTargets(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("documents", out var map) && map.ValueKind == JsonValueKind.Object
                ? [.. map.EnumerateObject().Select(p => p.Value.GetString() ?? string.Empty)]
                : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string? Metadata(XDocument nuspec, string name) =>
        nuspec.Descendants().FirstOrDefault(e => e.Name.LocalName == name)?.Value.Trim();

    private static ReleaseCheckResult Result(string name, List<string> problems, string success) =>
        problems.Count == 0
            ? new(name, true, success)
            : new(name, false, string.Join(Environment.NewLine + "  ", problems.Prepend(string.Create(CultureInfo.InvariantCulture, $"{problems.Count} problem(s)"))));

    [GeneratedRegex("^[0-9a-f]{40}$", RegexOptions.IgnoreCase)]
    private static partial Regex CommitPattern();

    [GeneratedRegex(@"\.git$", RegexOptions.IgnoreCase)]
    private static partial Regex RepositorySuffix();

    private sealed record Package(string Id, string Version, string Path, string? SymbolsPath, XDocument Nuspec, HashSet<string> Entries);

    private sealed record Pdb(string? SourceLink, IReadOnlyList<string> Documents);
}
