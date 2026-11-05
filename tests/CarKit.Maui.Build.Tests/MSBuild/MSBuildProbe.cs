using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Security;
using System.Text;
using System.Text.Json;

namespace CarKit.Maui.Build.Tests.MSBuild;

// A project without an SDK that imports buildTransitive/CarKit.Maui.targets the way NuGet does (after the project
// body), and runs the given target through `dotnet msbuild -getItem/-getProperty`.
internal static class MSBuildProbe
{
    private static readonly string[] _items =
    [
        "CarKitCarPlayEntitlement", "CarKitAndroidIntentCategory", "CarKitAndroidPermission", "CarKitAutomotiveUses",
        "CustomEntitlements", "PartialAppManifest", "AndroidResource", "RuntimeHostConfigurationOption", "CompilerVisibleProperty",
    ];

    private static readonly string[] _properties =
    [
        "CarKitNormalizedCategories", "CarKitAndroidGenerateService", "CarKitAndroidIntentCategories", "CarKitAndroidPermissions",
        "CarKitAndroidMinCarApiLevel", "CarKitAndroidAutomotiveDescriptor", "CollectAppManifestsDependsOn",
        "CarKitAndroidMediaService", "CarKitAndroidMediaPermissions", "CarKitAndroidMessageReceiver", "CarKitAndroidMessagePermissions",
        "CodesignRequireProvisioningProfile",
    ];

    private static readonly string _targetsPath = typeof(MSBuildProbe).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(a => a.Key == "CarKitTargets").Value!;

    public static ProbeResult Run(string platform, string target, IReadOnlyDictionary<string, string> properties, bool app = true)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "probes", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var project = new StringBuilder()
            .AppendLine("<Project>")
            .AppendLine("  <PropertyGroup>")
            .AppendLine(CultureInfo.InvariantCulture, $"    <TargetFramework>net10.0-{platform}</TargetFramework>")
            .AppendLine("    <IntermediateOutputPath>obj/</IntermediateOutputPath>")
            .AppendLine("    <Configuration>Debug</Configuration>");
        if (app)
        {
            project.AppendLine("    <OutputType>Exe</OutputType>");
        }

        foreach (var (name, value) in properties)
        {
            project.AppendLine(CultureInfo.InvariantCulture, $"    <{name}>{SecurityElement.Escape(value)}</{name}>");
        }

        project.AppendLine("  </PropertyGroup>")
            .AppendLine(CultureInfo.InvariantCulture, $"  <Import Project=\"{_targetsPath}\" />")
            .AppendLine("</Project>");
        var projectPath = Path.Combine(directory, "probe.proj");
        File.WriteAllText(projectPath, project.ToString());

        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            // Repo directory: global.json selects the same SDK as the build
            WorkingDirectory = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(_targetsPath)!, "..", "..", "..")),
        };
        foreach (var key in start.Environment.Keys.Where(k => k.StartsWith("MSBuild", StringComparison.OrdinalIgnoreCase)).ToList())
        {
            start.Environment.Remove(key);
        }

        foreach (var argument in new[] { "msbuild", projectPath, "-nologo", "-nodeReuse:false", $"-t:{target}", $"-getItem:{string.Join(',', _items)}", $"-getProperty:{string.Join(',', _properties)}" })
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(TimeSpan.FromMinutes(2)))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("dotnet msbuild did not finish.");
        }

        return ProbeResult.Parse(directory, process.ExitCode, output.Result + error.Result);
    }
}

internal sealed record ProbeItem(string Identity, IReadOnlyDictionary<string, string> Metadata)
{
    public string this[string name] => Metadata.TryGetValue(name, out var value) ? value : string.Empty;
}

internal sealed class ProbeResult
{
    private ProbeResult(string directory, int exitCode, string log, Dictionary<string, List<ProbeItem>> items, Dictionary<string, string> properties)
    {
        Directory = directory;
        ExitCode = exitCode;
        Log = log;
        Items = items;
        Properties = properties;
    }

    public string Directory { get; }

    public int ExitCode { get; }

    public string Log { get; }

    public Dictionary<string, List<ProbeItem>> Items { get; }

    public Dictionary<string, string> Properties { get; }

    public IReadOnlyList<ProbeItem> this[string itemType] => Items.TryGetValue(itemType, out var items) ? items : [];

    public string Property(string name) => Properties.TryGetValue(name, out var value) ? value : string.Empty;

    public IEnumerable<string> Identities(string itemType) => this[itemType].Select(i => i.Identity);

    public bool HasDiagnostic(string kind, string code) => Log.Contains($"{kind} {code}:", StringComparison.Ordinal);

    public string ReadFile(string relativePath) => File.ReadAllText(Path.Combine(Directory, relativePath));

    public static ProbeResult Parse(string directory, int exitCode, string log)
    {
        var items = new Dictionary<string, List<ProbeItem>>();
        var properties = new Dictionary<string, string>();
        var start = log.IndexOf("\n{", StringComparison.Ordinal);
        start = log.StartsWith('{') ? 0 : start < 0 ? -1 : start + 1;
        if (start >= 0)
        {
            using var json = JsonDocument.Parse(log[start..log.LastIndexOf('}')].TrimEnd() + "}");
            if (json.RootElement.TryGetProperty(nameof(Properties), out var props))
            {
                foreach (var p in props.EnumerateObject())
                {
                    properties[p.Name] = p.Value.GetString() ?? string.Empty;
                }
            }

            if (json.RootElement.TryGetProperty(nameof(Items), out var types))
            {
                foreach (var type in types.EnumerateObject())
                {
                    items[type.Name] = [.. type.Value.EnumerateArray().Select(i => new ProbeItem(
                        i.GetProperty("Identity").GetString()!,
                        i.EnumerateObject().ToDictionary(m => m.Name, m => m.Value.GetString() ?? string.Empty)))];
                }
            }
        }

        return new ProbeResult(directory, exitCode, log, items, properties);
    }
}
