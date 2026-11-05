using CarKit.Maui;

namespace CarKit.Sample;

// State shown on the phone screen: the CarKit configuration from the build, the app and car lifecycle, deep links
public static class SampleDiagnostics
{
    private static readonly Lock _sync = new();

    public static event EventHandler? Changed;

    public static int SleepCount { get; private set; }

    public static int ResumeCount { get; private set; }

    public static string? LastAppLink { get; private set; }

    public static string CarState { get; private set; } = "not connected";

    // Last event from the car screens (tab selection, search, validator message)
    public static string? LastCarEvent { get; private set; }

    // Value of <CarKitCategories> passed by MSBuild (RuntimeHostConfigurationOption)
    public static string ProjectCategories => AppContext.GetData("CarKit.Maui.Categories") as string ?? "(none)";

    public static void Sleep() => Update(() => SleepCount++);

    public static void Resume() => Update(() => ResumeCount++);

    public static void AppLink(Uri uri) => Update(() => LastAppLink = uri.ToString());

    public static void CarConnected(CarPlatform platform) => Update(() => CarState = $"connected ({platform})");

    public static void CarDisconnected() => Update(() => CarState = "disconnected");

    public static void Log(string carEvent) => Update(() => LastCarEvent = carEvent);

    private static void Update(Action change)
    {
        lock (_sync)
        {
            change();
        }

        Changed?.Invoke(null, EventArgs.Empty);
    }
}
