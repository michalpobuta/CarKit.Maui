using System.Diagnostics;

namespace CarKit.Maui.DeviceTests;

internal sealed record DeviceTest(string Name, Func<Task> Run);

// Minimal runner without reflection: the test list is in code (DeviceTestCatalog), each test on the main thread with a
// time limit. The "PASS/FAIL name" lines and the "CARKIT-DEVICE-TESTS: PASS n FAIL m" summary go to stdout (iOS) and
// logcat (Android).
internal static class DeviceTestRunner
{
    public const string Tag = "CarKit.DeviceTests";
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(20);

    // Stage 10: investigating flaky tests: CARKIT_DEVICE_TESTS_FILTER (a substring of the name) and
    // CARKIT_DEVICE_TESTS_REPEAT (the number of repetitions of each selected test in one process); iOS: SIMCTL_CHILD_*,
    // Android: intent extras (MainActivity)
    public static IReadOnlyList<DeviceTest> Select(IReadOnlyList<DeviceTest> all)
    {
        var filter = Environment.GetEnvironmentVariable("CARKIT_DEVICE_TESTS_FILTER");
        var selected = string.IsNullOrEmpty(filter) ? all : [.. all.Where(t => t.Name.Contains(filter, StringComparison.Ordinal))];
        var repeat = int.TryParse(Environment.GetEnvironmentVariable("CARKIT_DEVICE_TESTS_REPEAT"), out var count) && count > 1 ? count : 1;
        return repeat == 1 ? selected : [.. Enumerable.Range(0, repeat).SelectMany(_ => selected)];
    }

    public static async Task<int> RunAsync(IReadOnlyList<DeviceTest> tests, Action<string> report)
    {
        int passed = 0, failed = 0;
        Write($"CARKIT-DEVICE-TESTS: START {tests.Count}");
        foreach (var test in tests)
        {
            var watch = Stopwatch.StartNew();
            try
            {
                await test.Run().WaitAsync(_timeout);
                passed++;
                Write($"PASS {test.Name} ({watch.ElapsedMilliseconds} ms)");
            }
            catch (Exception ex)
            {
                failed++;
                Write($"FAIL {test.Name}: {ex.GetType().Name}: {ex.Message}");
                Write(ex.StackTrace ?? string.Empty);
                report($"FAIL {test.Name}");
            }
        }

        var summary = $"CARKIT-DEVICE-TESTS: PASS {passed} FAIL {failed}";
        Write(summary);
        report(summary);
        return failed;
    }

    // Stage 8: results of probes and measurements (e.g. frame times) in the test output; the script prints the "INFO " lines
    public static void Info(string line) => Write("INFO " + line);

    public static void Exit(int failed)
    {
#if ANDROID
        Java.Lang.JavaSystem.Exit(failed);
#else
        Environment.Exit(failed);
#endif
    }

    private static void Write(string line)
    {
#if ANDROID
        Android.Util.Log.Info(Tag, line);
#else
        Console.WriteLine(line);
#endif
    }
}
