using CarKit.Maui.Communication;
using CarKit.Maui.Tests.Hosting;
using CarKit.Maui.Tests.Media;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CarKit.Maui.Tests.Communication;

// UseCommunication (spec: "Communication module decisions (Stage 6)", Registration)
public class CommunicationBuilderTests
{
    private static ServiceProvider Register(Action<CarCommunicationBuilder> configure, string? appGroup = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICarDispatcher>(new InlineDispatcher());
        CarKitAppBuilderExtensions.Register(
            services,
            car => CarKitCommunicationBuilderExtensions.UseCommunication(car.UseHandler<RecordingHandler>(), configure, appGroup),
            CarAppCategory.Communication);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Communication_registers_router_calls_and_options()
    {
        using var provider = Register(_ => { });

        var runtime = provider.GetRequiredService<CarCommunicationRuntime>();
        Assert.Same(provider.GetRequiredService<CarMessageRouter>(), runtime.Messages);
        Assert.Same(provider.GetRequiredService<CarCallRegistry>(), runtime.Calls);
        Assert.Same(CarDefaultCallHandler.Instance, runtime.CallHandler);
        // net10.0 has no platform: a call manager without support (like Android until the Core-Telecom binding)
        Assert.False(provider.GetRequiredService<ICarCallManager>().IsSupported);
        Assert.Null(provider.GetService<ICarMessageHandler>());
        // The module does not drive car screens
        Assert.IsType<RecordingHandler>(provider.GetRequiredService<ICarAppHandler>());
    }

    [Fact]
    public void Handlers_resolve_to_the_same_instance()
    {
        using var provider = Register(c => c.UseMessageHandler<TestMessageHandler>().UseCallHandler<TestCallHandler>());

        Assert.Same(provider.GetRequiredService<TestMessageHandler>(), provider.GetRequiredService<ICarMessageHandler>());
        Assert.Same(provider.GetRequiredService<TestCallHandler>(), provider.GetRequiredService<ICarCallHandler>());
        Assert.Same(provider.GetRequiredService<TestCallHandler>(), provider.GetRequiredService<CarCommunicationRuntime>().CallHandler);
    }

    [Fact]
    public void Use_communication_twice_fails()
    {
        var services = new ServiceCollection();
        var error = Assert.Throws<InvalidOperationException>(() => CarKitAppBuilderExtensions.Register(
            services,
            car => car.UseHandler<RecordingHandler>().UseCommunication(_ => { }).UseCommunication(_ => { }),
            CarAppCategory.Communication));

        Assert.Contains("UseCommunication", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Options_default_and_reject_empty_labels()
    {
        using var provider = Register(_ => { });
        var options = provider.GetRequiredService<CarCommunicationOptions>();

        Assert.Equal(new CarCommunicationOptions("Messages", "Reply", "Mark as read", null, true, null), options);
        Assert.Contains("ReplyActionTitle", Assert.Throws<InvalidOperationException>(() => Register(c => c.ReplyActionTitle = " ")).Message, StringComparison.Ordinal);
        Assert.Contains("NotificationChannelName", Assert.Throws<InvalidOperationException>(() => Register(c => c.NotificationChannelName = "")).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Options_keep_the_app_values()
    {
        using var provider = Register(c =>
        {
            c.NotificationChannelName = "Messages";
            c.ReplyActionTitle = "Reply";
            c.MarkAsReadActionTitle = "Read";
            c.AndroidSmallIcon = "ic_message";
            c.IncludeCallsInRecents = false;
        });

        Assert.Equal(
            new CarCommunicationOptions("Messages", "Reply", "Read", "ic_message", false, null),
            provider.GetRequiredService<CarCommunicationOptions>());
    }

    [Fact]
    public void App_group_comes_from_the_build()
    {
        using var provider = Register(_ => { }, appGroup: " group.com.carkit.sample ");

        Assert.Equal("group.com.carkit.sample", provider.GetRequiredService<CarCommunicationOptions>().AppGroup);
        Assert.Equal("CarKit.Maui.Communication.AppGroup", CarCommunicationBuildConfiguration.AppGroupSwitchName);
        Assert.Null(CarCommunicationBuildConfiguration.AppGroup);
    }
}
