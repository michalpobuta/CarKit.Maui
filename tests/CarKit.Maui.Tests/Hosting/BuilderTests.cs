using CarKit.Maui.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Hosting;
using Xunit;

namespace CarKit.Maui.Tests.Hosting;

public class BuilderTests
{
    [Fact]
    public void UseCarKit_registers_options_runtime_and_handler()
    {
        var services = new ServiceCollection();
        CarKitAppBuilderExtensions.Register(services, car => car.UseHandler<RecordingHandler>(), CarAppCategory.Parking);
        using var provider = services.BuildServiceProvider();

        Assert.Equal(CarAppCategory.Parking, provider.GetRequiredService<CarKitOptions>().Categories);
        var handler = Assert.IsType<RecordingHandler>(provider.GetRequiredService<ICarAppHandler>());
        Assert.Same(handler, provider.GetRequiredService<ICarAppHandler>());
        Assert.IsType<MauiCarDispatcher>(provider.GetRequiredService<ICarDispatcher>());
        Assert.NotNull(provider.GetRequiredService<ICarErrorHandler>());
        Assert.NotNull(provider.GetRequiredService<CarKitRuntime>());
    }

    [Fact]
    public void UseCarKit_on_maui_builder_reads_project_categories_from_app_context()
    {
        AppContext.SetData(CarKitBuildConfiguration.CategoriesSwitchName, "Parking;Audio");
        try
        {
            var builder = MauiApp.CreateBuilder(useDefaults: false);
            Assert.Same(builder, builder.UseCarKit(car => car.UseHandler<RecordingHandler>()));
            using var provider = builder.Services.BuildServiceProvider();
            Assert.Equal(CarAppCategory.Parking | CarAppCategory.Audio, provider.GetRequiredService<CarKitOptions>().Categories);
        }
        finally
        {
            AppContext.SetData(CarKitBuildConfiguration.CategoriesSwitchName, null);
        }
    }

    [Fact]
    public void Builder_forwards_options()
    {
        var services = new ServiceCollection();
        CarKitAppBuilderExtensions.Register(services, car =>
        {
            Assert.Same(services, car.Services);
            car.UseHandler<RecordingHandler>();
            car.UnsupportedTemplateBehavior = UnsupportedTemplateBehavior.Throw;
            car.Validation = CarValidationMode.Off;
            car.Limits.OnOverflow = CarOverflowBehavior.Throw;
            car.Diagnostics.LogNativeCalls = true;
            car.Android.AllowedHosts.Add("abc,com.example.host");
        }, CarAppCategory.Parking);
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<CarKitOptions>();
        Assert.Equal(UnsupportedTemplateBehavior.Throw, options.UnsupportedTemplateBehavior);
        Assert.Equal(CarValidationMode.Off, options.Validation);
        Assert.Equal(CarOverflowBehavior.Throw, options.Limits.OnOverflow);
        Assert.True(options.Diagnostics.LogNativeCalls);
        Assert.Equal(["abc,com.example.host"], provider.GetRequiredService<CarKitAndroidOptions>().AllowedHosts);
    }

    [Fact]
    public void App_services_registered_before_UseCarKit_win()
    {
        using var dispatcher = new FakeCarDispatcher();
        var services = new ServiceCollection();
        services.AddSingleton<ICarDispatcher>(dispatcher);
        CarKitAppBuilderExtensions.Register(services, car => car.UseHandler<RecordingHandler>(), CarAppCategory.Parking);
        using var provider = services.BuildServiceProvider();

        Assert.Same(dispatcher, provider.GetRequiredService<ICarDispatcher>());
    }

    [Fact]
    public void Code_categories_must_match_project_categories()
    {
        var mismatch = Assert.Throws<InvalidOperationException>(() => CarKitAppBuilderExtensions.Register(new ServiceCollection(), car =>
        {
            car.Categories = CarAppCategory.Audio;
            car.UseHandler<RecordingHandler>();
        }, CarAppCategory.Parking));
        Assert.Contains("CarKitCategories", mismatch.Message, StringComparison.Ordinal);

        CarKitAppBuilderExtensions.Register(new ServiceCollection(), car =>
        {
            car.Categories = CarAppCategory.Parking;
            car.UseHandler<RecordingHandler>();
        }, CarAppCategory.Parking);
    }

    [Fact]
    public void Categories_are_required()
    {
        var missing = Assert.Throws<InvalidOperationException>(() =>
            CarKitAppBuilderExtensions.Register(new ServiceCollection(), car => car.UseHandler<RecordingHandler>(), buildCategories: null));
        Assert.Contains("CarKitCategories", missing.Message, StringComparison.Ordinal);

        var services = new ServiceCollection();
        CarKitAppBuilderExtensions.Register(services, car =>
        {
            car.Categories = CarAppCategory.Parking;
            car.UseHandler<RecordingHandler>();
        }, buildCategories: null);
        using var provider = services.BuildServiceProvider();
        Assert.Equal(CarAppCategory.Parking, provider.GetRequiredService<CarKitOptions>().Categories);
    }

    [Fact]
    public void Handler_is_required()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            CarKitAppBuilderExtensions.Register(new ServiceCollection(), _ => { }, CarAppCategory.Parking));
        Assert.Contains("UseHandler", error.Message, StringComparison.Ordinal);
    }

    // Stage 5: a module (e.g. media.UseAutoBrowse()) can own the car screens instead of UseHandler
    [Fact]
    public void Module_handler_replaces_the_handler_requirement()
    {
        var services = new ServiceCollection();
        CarKitAppBuilderExtensions.Register(
            services, car => car.UseModuleHandler("media.UseAutoBrowse()", s => s.AddSingleton<ICarAppHandler, OtherHandler>()), CarAppCategory.Audio);
        using var provider = services.BuildServiceProvider();

        Assert.IsType<OtherHandler>(provider.GetRequiredService<ICarAppHandler>());
    }

    [Fact]
    public void Module_handler_and_app_handler_conflict()
    {
        var error = Assert.Throws<InvalidOperationException>(() => CarKitAppBuilderExtensions.Register(new ServiceCollection(), car =>
        {
            car.UseHandler<RecordingHandler>();
            car.UseModuleHandler("media.UseAutoBrowse()", s => s.AddSingleton<ICarAppHandler, OtherHandler>());
        }, CarAppCategory.Audio));
        Assert.Contains("UseHandler", error.Message, StringComparison.Ordinal);
        Assert.Contains("media.UseAutoBrowse()", error.Message, StringComparison.Ordinal);

        var services = new ServiceCollection();
        services.AddSingleton<ICarAppHandler, RecordingHandler>();
        Assert.Throws<InvalidOperationException>(() => CarKitAppBuilderExtensions.Register(
            services, car => car.UseModuleHandler("media.UseAutoBrowse()", s => s.AddSingleton<ICarAppHandler, OtherHandler>()), CarAppCategory.Audio));
    }

    [Fact]
    public void Modules_register_once()
    {
        var error = Assert.Throws<InvalidOperationException>(() => CarKitAppBuilderExtensions.Register(new ServiceCollection(), car =>
        {
            car.UseHandler<RecordingHandler>();
            car.AddModule("UseMedia");
            car.AddModule("UseMedia");
        }, CarAppCategory.Audio));
        Assert.Contains("UseMedia", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Malformed_allowed_hosts_fail_at_startup()
    {
        var error = Assert.Throws<InvalidOperationException>(() => CarKitAppBuilderExtensions.Register(new ServiceCollection(), car =>
        {
            car.UseHandler<RecordingHandler>();
            car.Android.AllowedHosts.Add("com.example.host");
        }, CarAppCategory.Parking));
        Assert.Contains("AllowedHosts", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UseHandler_twice_keeps_the_last_handler()
    {
        var services = new ServiceCollection();
        CarKitAppBuilderExtensions.Register(services, car => car.UseHandler<RecordingHandler>().UseHandler<OtherHandler>(), CarAppCategory.Parking);
        using var provider = services.BuildServiceProvider();

        Assert.IsType<OtherHandler>(provider.GetRequiredService<ICarAppHandler>());
    }

    [Fact]
    public void UseCarKit_twice_throws()
    {
        var services = new ServiceCollection();
        CarKitAppBuilderExtensions.Register(services, car => car.UseHandler<RecordingHandler>(), CarAppCategory.Parking);

        var error = Assert.Throws<InvalidOperationException>(() =>
            CarKitAppBuilderExtensions.Register(services, car => car.UseHandler<RecordingHandler>(), CarAppCategory.Parking));
        Assert.Contains("UseCarKit", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(" parking , Audio ")]
    [InlineData("Parking;audio")]
    [InlineData("Audio;Parking;")]
    public void Parse_normalizes_names(string value) =>
        Assert.Equal(CarAppCategory.Parking | CarAppCategory.Audio, CarKitBuildConfiguration.Parse(value));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ; ")]
    public void Parse_without_names_is_null(string? value) => Assert.Null(CarKitBuildConfiguration.Parse(value));

    [Theory]
    [InlineData("Poi")]
    [InlineData("None")]
    [InlineData("8")]
    public void Parse_rejects_unknown_names(string value)
    {
        var error = Assert.Throws<InvalidOperationException>(() => CarKitBuildConfiguration.Parse(value));
        Assert.Contains(value, error.Message, StringComparison.Ordinal);
    }
}
