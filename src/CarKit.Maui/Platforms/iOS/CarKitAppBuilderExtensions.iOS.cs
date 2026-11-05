using System.Diagnostics.CodeAnalysis;
using Microsoft.Maui.Hosting;

namespace CarKit.Maui;

public static partial class CarKitAppBuilderExtensions
{
    // Scene delegates are referenced only in Info.plist (by name); without this ILLink would remove them in Release
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(CarKitSceneDelegate))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(CarKitPhoneSceneDelegate))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(CarKitDashboardSceneDelegate))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(CarKitInstrumentClusterSceneDelegate))]
    static partial void ConfigurePlatform(MauiAppBuilder builder)
    {
    }
}
