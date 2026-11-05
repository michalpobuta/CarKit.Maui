namespace CarKit.Maui.Media;

// Values from MSBuild (buildTransitive/CarKit.Maui.targets) passed through RuntimeHostConfigurationOption
internal static class CarKitMediaBuildConfiguration
{
    public const string TemplatedMediaSwitchName = "CarKit.Maui.Media.TemplatedMedia";

    // <CarKitAndroidTemplatedMedia>: no entry = false (AA media templates are in Early Access)
    public static bool TemplatedMedia => AppContext.TryGetSwitch(TemplatedMediaSwitchName, out var enabled) && enabled;
}
