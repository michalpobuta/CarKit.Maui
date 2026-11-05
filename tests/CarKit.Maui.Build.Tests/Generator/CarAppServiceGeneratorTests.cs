using Xunit;

namespace CarKit.Maui.Build.Tests.Generator;

public class CarAppServiceGeneratorTests
{
    private const string ServiceAttribute = """[global::Android.App.Service(Name = "carkit.maui.generated.CarKitGeneratedAppService", Exported = true)]""";
    private const string DescriptorAttribute = """[assembly: global::Android.App.MetaData("com.google.android.gms.car.application", Resource = "@xml/automotive_app_desc")]""";

    [Fact]
    public void Generates_service_for_poi_app()
    {
        var run = GeneratorHarness.Run(new Dictionary<string, string>
        {
            ["CarKitAndroidGenerateService"] = "true",
            ["CarKitAndroidIntentCategories"] = "androidx.car.app.category.POI",
            ["CarKitAndroidPermissions"] = "androidx.car.app.MAP_TEMPLATES",
            ["CarKitAndroidMinCarApiLevel"] = "3",
            ["CarKitAndroidAutomotiveDescriptor"] = "true",
        });

        var source = Assert.Single(run.Sources);
        Assert.Equal("CarKitGeneratedAppService.g.cs", source.HintName);
        var text = source.SourceText.ToString();
        Assert.Contains(ServiceAttribute, text, StringComparison.Ordinal);
        Assert.Contains("""[global::Android.App.IntentFilter(new[] { "androidx.car.app.CarAppService" }, Categories = new[] { "androidx.car.app.category.POI" })]""", text, StringComparison.Ordinal);
        Assert.Contains("internal sealed class CarKitGeneratedAppService : global::CarKit.Maui.CarKitAppServiceBase", text, StringComparison.Ordinal);
        Assert.Contains("""[assembly: global::Android.App.MetaData("androidx.car.app.minCarApiLevel", Value = "3")]""", text, StringComparison.Ordinal);
        Assert.Contains("""[assembly: global::Android.App.UsesPermission("androidx.car.app.MAP_TEMPLATES")]""", text, StringComparison.Ordinal);
        Assert.Contains(DescriptorAttribute, text, StringComparison.Ordinal);
        Assert.Empty(run.Diagnostics);
    }

    // Stage 7: IoT and weather: a CarAppService with both categories, without map permissions
    [Fact]
    public void Iot_and_weather_app_gets_template_service()
    {
        var run = GeneratorHarness.Run(new Dictionary<string, string>
        {
            ["CarKitAndroidGenerateService"] = "true",
            ["CarKitAndroidIntentCategories"] = "androidx.car.app.category.IOT,androidx.car.app.category.WEATHER",
            ["CarKitAndroidPermissions"] = "",
            ["CarKitAndroidAutomotiveDescriptor"] = "true",
        });

        var text = Assert.Single(run.Sources).SourceText.ToString();
        Assert.Contains("""Categories = new[] { "androidx.car.app.category.IOT", "androidx.car.app.category.WEATHER" }""", text, StringComparison.Ordinal);
        Assert.DoesNotContain("UsesPermission", text, StringComparison.Ordinal);
        Assert.Contains(DescriptorAttribute, text, StringComparison.Ordinal);
        Assert.Empty(run.Diagnostics);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("false")]
    [InlineData("")]
    public void Generates_nothing_when_disabled(string? generate)
    {
        var properties = new Dictionary<string, string>
        {
            ["CarKitAndroidIntentCategories"] = "androidx.car.app.category.POI",
            ["CarKitAndroidAutomotiveDescriptor"] = "true",
        };
        if (generate is not null)
        {
            properties["CarKitAndroidGenerateService"] = generate;
        }

        Assert.Empty(GeneratorHarness.Run(properties).Sources);
    }

    [Fact]
    public void Media_only_app_gets_descriptor_without_service()
    {
        var run = GeneratorHarness.Run(new Dictionary<string, string>
        {
            ["CarKitAndroidGenerateService"] = "true",
            ["CarKitAndroidIntentCategories"] = "",
            ["CarKitAndroidPermissions"] = "",
            ["CarKitAndroidMinCarApiLevel"] = "1",
            ["CarKitAndroidAutomotiveDescriptor"] = "true",
        });

        var text = Assert.Single(run.Sources).SourceText.ToString();
        Assert.Contains(DescriptorAttribute, text, StringComparison.Ordinal);
        Assert.DoesNotContain("class CarKitGeneratedAppService", text, StringComparison.Ordinal);
        Assert.DoesNotContain("minCarApiLevel", text, StringComparison.Ordinal);
        Assert.Empty(run.Diagnostics);
    }

    [Fact]
    public void Multiple_categories_keep_order_without_duplicates()
    {
        var run = GeneratorHarness.Run(new Dictionary<string, string>
        {
            ["CarKitAndroidGenerateService"] = "true",
            ["CarKitAndroidIntentCategories"] = " androidx.car.app.category.MESSAGING ;androidx.car.app.category.CALLING;androidx.car.app.category.MESSAGING",
            ["CarKitAndroidPermissions"] = "androidx.car.app.NAVIGATION_TEMPLATES;androidx.car.app.ACCESS_SURFACE;androidx.car.app.NAVIGATION_TEMPLATES",
        });

        var text = Assert.Single(run.Sources).SourceText.ToString();
        Assert.Contains("""Categories = new[] { "androidx.car.app.category.MESSAGING", "androidx.car.app.category.CALLING" }""", text, StringComparison.Ordinal);
        Assert.Equal(2, text.Split("UsesPermission(").Length - 1);
        Assert.DoesNotContain(DescriptorAttribute, text, StringComparison.Ordinal);
        Assert.Empty(run.Diagnostics);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("abc")]
    [InlineData("0")]
    public void Invalid_min_car_api_level_defaults_to_1(string? level)
    {
        var properties = new Dictionary<string, string>
        {
            ["CarKitAndroidGenerateService"] = "true",
            ["CarKitAndroidIntentCategories"] = "androidx.car.app.category.IOT",
        };
        if (level is not null)
        {
            properties["CarKitAndroidMinCarApiLevel"] = level;
        }

        var text = Assert.Single(GeneratorHarness.Run(properties).Sources).SourceText.ToString();
        Assert.Contains("""MetaData("androidx.car.app.minCarApiLevel", Value = "1")""", text, StringComparison.Ordinal);
    }

    // Stage 5: Audio with the Media module: a media service (Media3) with both intent filters and foreground
    // permissions, no CarAppService
    private const string MediaServiceAttribute =
        """[global::Android.App.Service(Name = "carkit.maui.generated.CarKitGeneratedMediaLibraryService", Exported = true, ForegroundServiceType = global::Android.Content.PM.ForegroundService.TypeMediaPlayback)]""";

    [Fact]
    public void Audio_app_gets_the_media_service_without_car_app_service()
    {
        var run = GeneratorHarness.Run(new Dictionary<string, string>
        {
            ["CarKitAndroidGenerateService"] = "true",
            ["CarKitAndroidIntentCategories"] = "",
            ["CarKitAndroidPermissions"] = "",
            ["CarKitAndroidAutomotiveDescriptor"] = "true",
            ["CarKitAndroidMediaService"] = "true",
            ["CarKitAndroidMediaPermissions"] = "android.permission.FOREGROUND_SERVICE,android.permission.FOREGROUND_SERVICE_MEDIA_PLAYBACK",
        });

        var text = Assert.Single(run.Sources).SourceText.ToString();
        Assert.Contains(MediaServiceAttribute, text, StringComparison.Ordinal);
        Assert.Contains("""[global::Android.App.IntentFilter(new[] { "androidx.media3.session.MediaLibraryService", "android.media.browse.MediaBrowserService" })]""", text, StringComparison.Ordinal);
        Assert.Contains("internal sealed class CarKitGeneratedMediaLibraryService : global::CarKit.Maui.Media.CarKitMediaLibraryService", text, StringComparison.Ordinal);
        Assert.Contains("""[assembly: global::Android.App.UsesPermission("android.permission.FOREGROUND_SERVICE")]""", text, StringComparison.Ordinal);
        Assert.Contains("""[assembly: global::Android.App.UsesPermission("android.permission.FOREGROUND_SERVICE_MEDIA_PLAYBACK")]""", text, StringComparison.Ordinal);
        Assert.Contains(DescriptorAttribute, text, StringComparison.Ordinal);
        Assert.DoesNotContain("class CarKitGeneratedAppService", text, StringComparison.Ordinal);
        Assert.DoesNotContain("minCarApiLevel", text, StringComparison.Ordinal);
        Assert.Empty(run.Diagnostics);
    }

    // Stage 6: Communication with the module: a notification action receiver and POST_NOTIFICATIONS, CarAppService
    // with MESSAGING only
    [Fact]
    public void Communication_app_gets_the_message_receiver()
    {
        var run = GeneratorHarness.Run(new Dictionary<string, string>
        {
            ["CarKitAndroidGenerateService"] = "true",
            ["CarKitAndroidIntentCategories"] = "androidx.car.app.category.MESSAGING",
            ["CarKitAndroidPermissions"] = "",
            ["CarKitAndroidAutomotiveDescriptor"] = "true",
            ["CarKitAndroidMessageReceiver"] = "true",
            ["CarKitAndroidMessagePermissions"] = "android.permission.POST_NOTIFICATIONS",
        });

        var text = Assert.Single(run.Sources).SourceText.ToString();
        Assert.Contains("""[global::Android.Content.BroadcastReceiver(Name = "carkit.maui.generated.CarKitGeneratedMessageReceiver", Exported = false)]""", text, StringComparison.Ordinal);
        Assert.Contains("internal sealed class CarKitGeneratedMessageReceiver : global::CarKit.Maui.Communication.CarKitMessageActionReceiver", text, StringComparison.Ordinal);
        Assert.Contains("""[assembly: global::Android.App.UsesPermission("android.permission.POST_NOTIFICATIONS")]""", text, StringComparison.Ordinal);
        Assert.Contains("""Categories = new[] { "androidx.car.app.category.MESSAGING" }""", text, StringComparison.Ordinal);
        Assert.DoesNotContain("CALLING", text, StringComparison.Ordinal);
        Assert.Empty(run.Diagnostics);
    }

    [Fact]
    public void Message_receiver_alone_is_generated()
    {
        var run = GeneratorHarness.Run(new Dictionary<string, string>
        {
            ["CarKitAndroidGenerateService"] = "true",
            ["CarKitAndroidMessageReceiver"] = "true",
            ["CarKitAndroidMessagePermissions"] = "android.permission.POST_NOTIFICATIONS",
        });

        var text = Assert.Single(run.Sources).SourceText.ToString();
        Assert.Contains("class CarKitGeneratedMessageReceiver", text, StringComparison.Ordinal);
        Assert.DoesNotContain("class CarKitGeneratedAppService", text, StringComparison.Ordinal);
        Assert.Empty(run.Diagnostics);
    }

    [Fact]
    public void Mixed_app_gets_both_services()
    {
        var run = GeneratorHarness.Run(new Dictionary<string, string>
        {
            ["CarKitAndroidGenerateService"] = "true",
            ["CarKitAndroidIntentCategories"] = "androidx.car.app.category.POI,androidx.car.app.category.MEDIA",
            ["CarKitAndroidPermissions"] = "androidx.car.app.MAP_TEMPLATES,androidx.car.app.MEDIA_TEMPLATES",
            ["CarKitAndroidAutomotiveDescriptor"] = "true",
            ["CarKitAndroidMediaService"] = "true",
            ["CarKitAndroidMediaPermissions"] = "android.permission.FOREGROUND_SERVICE;android.permission.FOREGROUND_SERVICE_MEDIA_PLAYBACK",
        });

        var text = Assert.Single(run.Sources).SourceText.ToString();
        Assert.Contains(ServiceAttribute, text, StringComparison.Ordinal);
        Assert.Contains(MediaServiceAttribute, text, StringComparison.Ordinal);
        Assert.Contains("""Categories = new[] { "androidx.car.app.category.POI", "androidx.car.app.category.MEDIA" }""", text, StringComparison.Ordinal);
        Assert.Equal(4, text.Split("UsesPermission(").Length - 1);
        Assert.Empty(run.Diagnostics);
    }
}
