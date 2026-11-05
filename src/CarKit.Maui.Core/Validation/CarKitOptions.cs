namespace CarKit.Maui;

/// <summary>App categories; they drive validation and the generated entitlements and manifest entries.</summary>
[Flags]
#pragma warning disable CA1714 // The name from the specification (CarAppCategory) is binding
public enum CarAppCategory
#pragma warning restore CA1714
{
    /// <summary>No category.</summary>
    None = 0,

    /// <summary>Audio (CarPlay audio, Android Auto media).</summary>
    Audio = 1 << 0,

    /// <summary>Communication (CarPlay communication, Android Auto messaging and calling).</summary>
    Communication = 1 << 1,

    /// <summary>Navigation (CarPlay maps, Android Auto navigation).</summary>
    Navigation = 1 << 2,

    /// <summary>Parking (CarPlay parking, Android Auto POI).</summary>
    Parking = 1 << 3,

    /// <summary>EV charging (CarPlay charging, Android Auto POI).</summary>
    EvCharging = 1 << 4,

    /// <summary>Fueling (CarPlay fueling, Android Auto POI).</summary>
    Fueling = 1 << 5,

    /// <summary>Quick food ordering (CarPlay only).</summary>
    QuickFoodOrdering = 1 << 6,

    /// <summary>Driving task (CarPlay only).</summary>
    DrivingTask = 1 << 7,

    /// <summary>Voice-based conversational apps (CarPlay, iOS 26.4+).</summary>
    VoiceConversational = 1 << 8,

    /// <summary>Video, parked only (CarPlay, iOS 27+).</summary>
    Video = 1 << 9,

    /// <summary>Public safety (CarPlay only).</summary>
    PublicSafety = 1 << 10,

    /// <summary>Internet of things (Android Auto only).</summary>
    Iot = 1 << 11,

    /// <summary>Weather (Android Auto only).</summary>
    Weather = 1 << 12,
}

/// <summary>What happens when a template breaks a platform rule.</summary>
public enum CarValidationMode
{
    /// <summary>Throw before the template reaches the platform.</summary>
    Strict,

    /// <summary>Log a warning and try to show the template anyway.</summary>
    Warn,

    /// <summary>Skip category and limit checks.</summary>
    Off,
}

/// <summary>What happens when a template has more items than the platform shows.</summary>
public enum CarOverflowBehavior
{
    /// <summary>Drop the items over the limit.</summary>
    Truncate,

    /// <summary>Treat it as a validation error.</summary>
    Throw,
}

/// <summary>What happens when the platform does not support a template.</summary>
public enum UnsupportedTemplateBehavior
{
    /// <summary>Use the template's fallback (explicit or built-in); throw when there is none.</summary>
    UseFallbackOrThrow,

    /// <summary>Always throw <see cref="CarTemplateNotSupportedException"/>.</summary>
    Throw,
}

/// <summary>CarKit configuration, set once when the app starts.</summary>
public sealed class CarKitOptions
{
    /// <summary>The <see cref="AppContext"/> key that sets the default <see cref="Validation"/>.</summary>
    public const string ValidationSwitchName = "CarKit.Maui.Validation";

    /// <summary>Gets or sets the app categories.</summary>
    public CarAppCategory Categories { get; set; }

    /// <summary>Gets or sets how unsupported templates are handled.</summary>
    public UnsupportedTemplateBehavior UnsupportedTemplateBehavior { get; set; }

    /// <summary>Gets or sets the validation mode. Defaults to the <see cref="ValidationSwitchName"/> app context value, or <see cref="CarValidationMode.Strict"/>.</summary>
    public CarValidationMode Validation { get; set; } = ResolveValidationMode(AppContext.GetData(ValidationSwitchName));

    /// <summary>Gets the limit options.</summary>
    public CarLimitOptions Limits { get; } = new();

    /// <summary>Gets the diagnostics options.</summary>
    public CarDiagnosticsOptions Diagnostics { get; } = new();

    internal static CarValidationMode ResolveValidationMode(object? configured) =>
        configured is string value && Enum.TryParse<CarValidationMode>(value, ignoreCase: true, out var mode)
            ? mode
            : CarValidationMode.Strict;
}

/// <summary>Content limit options.</summary>
public sealed class CarLimitOptions
{
    /// <summary>Gets or sets what happens when a template has too many items.</summary>
    public CarOverflowBehavior OnOverflow { get; set; }
}

/// <summary>Diagnostics options.</summary>
public sealed class CarDiagnosticsOptions
{
    /// <summary>Gets or sets a value indicating whether every platform call is logged at debug level.</summary>
    public bool LogNativeCalls { get; set; }
}
