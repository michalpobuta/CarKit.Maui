namespace CarKit.Maui.Communication;

// Values from MSBuild (buildTransitive/CarKit.Maui.targets) passed through RuntimeHostConfigurationOption
internal static class CarCommunicationBuildConfiguration
{
    public const string AppGroupSwitchName = "CarKit.Maui.Communication.AppGroup";

    // <CarKitIosAppGroup>: the App Group shared with the Intents Extension (Siri conversation store); none = no store
    public static string? AppGroup => AppContext.GetData(AppGroupSwitchName) as string;
}
