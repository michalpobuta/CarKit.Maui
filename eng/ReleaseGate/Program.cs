using System.Globalization;

namespace CarKit.ReleaseGate;

// Release gate: dotnet run --project eng/ReleaseGate -c Release -- --packages <dir> --tag v<version> --repo <repo directory>.
// Lines "PASS <check>: …"/"FAIL <check>: …", summary "RELEASE-GATE: PASS n FAIL m", exit code 1 on failure, 2 on bad arguments.
internal static class ReleaseGateProgram
{
    public static int Main(string[] args) => Run(args, Console.Out);

    public static int Run(IReadOnlyList<string> args, TextWriter output)
    {
        string? packages = null, tag = null, repository = Directory.GetCurrentDirectory();
        for (var i = 0; i < args.Count; i++)
        {
            var value = i + 1 < args.Count ? args[i + 1] : null;
            switch (args[i])
            {
                case "--packages" when value is not null:
                    packages = value;
                    i++;
                    break;
                case "--tag" when value is not null:
                    tag = value;
                    i++;
                    break;
                case "--repo" when value is not null:
                    repository = value;
                    i++;
                    break;
                default:
                    output.WriteLine($"Unknown or incomplete argument '{args[i]}'. Usage: --packages <dir> --tag v<version> [--repo <dir>]");
                    return 2;
            }
        }

        if (packages is null)
        {
            output.WriteLine("Usage: --packages <dir> --tag v<version> [--repo <dir>]");
            return 2;
        }

        var results = ReleaseChecks.Run(Path.GetFullPath(packages), tag, Path.GetFullPath(repository));
        foreach (var result in results)
        {
            output.WriteLine($"{(result.Passed ? "PASS" : "FAIL")} {result.Name}: {result.Message}");
        }

        var failed = results.Count(r => !r.Passed);
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"RELEASE-GATE: PASS {results.Count - failed} FAIL {failed}"));
        return failed == 0 ? 0 : 1;
    }
}
