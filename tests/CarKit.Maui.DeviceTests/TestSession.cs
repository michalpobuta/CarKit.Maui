using Microsoft.Extensions.Logging;

namespace CarKit.Maui.DeviceTests;

// A CarKit session on a stub host: the renderers get a real CarSession (handlers on the main thread through
// MauiCarDispatcher), and the host only records the calls. The platform and version decide the validator rules.
internal sealed class TestSession : ICarHost, ICarErrorHandler, ILogger
{
    private TestSession(CarPlatform platform, int? apiLevel, CarAppCategory categories = CarAppCategory.Parking)
    {
        Platform = platform;
        // Stage 9: the simulator OS version and the features of the asset the library was built with (CarFeature gate
        // as in the host)
        Capabilities = platform == CarPlatform.CarPlay
            ? new CarCapabilities(platform, IosVersion(), null, null, CarPlayAssetFeatures)
            : new CarCapabilities(platform, null, apiLevel ?? 8);
        Session = new CarSession(this, new CarKitOptions { Categories = categories, Validation = CarValidationMode.Strict }, new MauiCarDispatcher(), this, this);
    }

    public CarSession Session { get; }

#if IOS
    private static Version IosVersion() => new(Environment.OSVersion.Version.Major, Environment.OSVersion.Version.Minor);

    private static bool CarPlayAssetFeatures(CarFeature feature) => CarPlayFeatures.IsCompiled(feature);
#else
    private static Version IosVersion() => new(26, 0);

    private static bool CarPlayAssetFeatures(CarFeature feature) => CarFeatureDefaults.NewestAsset.Contains(feature);
#endif

    public List<string> Calls { get; } = [];

    // Templates sent to the host (SetRoot/Push/Update); auto-browse (Stage 5) then renders them with the real renderers
    public List<CarTemplate> Templates { get; } = [];

    public List<Exception> Errors { get; } = [];

    public List<string> Logs { get; } = [];

    public CarPlatform Platform { get; }

    public ICarCapabilities Capabilities { get; }

    public CarContentStyle ContentStyle => CarContentStyle.Light;

    public static TestSession CarPlay() => new(CarPlatform.CarPlay, null);

    public static TestSession AndroidAuto(int apiLevel) => new(CarPlatform.AndroidAuto, apiLevel);

    public static TestSession CarPlay(CarAppCategory categories) => new(CarPlatform.CarPlay, null, categories);

    public static TestSession AndroidAuto(int apiLevel, CarAppCategory categories) => new(CarPlatform.AndroidAuto, apiLevel, categories);

    public Task SetRootAsync(CarTemplate root, bool animated) => Record("SetRoot", root);

    public Task PushAsync(CarTemplate template, bool animated) => Record("Push", template);

    public Task PopAsync(bool animated) => Record("Pop");

    public Task PopToAsync(CarTemplate template, bool animated) => Record("PopTo");

    public Task PopToRootAsync(bool animated) => Record("PopToRoot");

    public Task PresentAsync(CarTemplate modal, bool animated) => Record("Present", modal);

    public Task DismissAsync(bool animated) => Record("Dismiss");

    public Task UpdateAsync(CarTemplate previous, CarTemplate updated) => Record("Update", updated);

    public TNative? GetNative<TNative>() where TNative : class => null;

    public void HandleError(Exception exception, CarErrorContext context) => Errors.Add(exception);

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        Logs.Add($"{logLevel}: {formatter(state, exception)} {exception?.GetType().Name} {exception?.Message}");

    private Task Record(string call, CarTemplate? template = null)
    {
        Calls.Add(call);
        if (template is not null)
        {
            Templates.Add(template);
        }

        return Task.CompletedTask;
    }
}
