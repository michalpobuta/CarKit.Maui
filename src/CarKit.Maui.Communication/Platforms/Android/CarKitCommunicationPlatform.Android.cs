using Microsoft.Extensions.DependencyInjection;

namespace CarKit.Maui.Communication;

public static partial class CarKitCommunicationBuilderExtensions
{
    // Android: MessagingStyle notifications with semantic actions (the generator generates the receiver); calls without
    // support until the Core-Telecom binding (CarUnsupportedCallManager from the neutral part)
    static partial void RegisterPlatform(IServiceCollection services, CarCommunicationOptions options)
    {
        services.AddSingleton(sp => new CarAndroidMessageNotifier(sp.GetRequiredService<CarCommunicationRuntime>(), Android.App.Application.Context));
        services.AddSingleton<ICarMessageNotifier>(static sp => sp.GetRequiredService<CarAndroidMessageNotifier>());
    }
}
