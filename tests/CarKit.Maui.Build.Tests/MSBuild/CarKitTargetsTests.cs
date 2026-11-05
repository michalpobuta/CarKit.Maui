using Xunit;

namespace CarKit.Maui.Build.Tests.MSBuild;

public class CarKitTargetsTests
{
    private const string Ios = "ios";
    private const string Android = "android";
    private const string ConfigureIos = "CarKitConfigureIos";
    private const string ConfigureAndroid = "CarKitConfigureAndroid";

    [Fact]
    public void Parking_on_ios_adds_parking_entitlement()
    {
        var result = Probe(Ios, ConfigureIos, "Parking");

        Assert.Equal(0, result.ExitCode);
        var entitlement = Assert.Single(result["CustomEntitlements"]);
        Assert.Equal("com.apple.developer.carplay-parking", entitlement.Identity);
        Assert.Equal("Boolean", entitlement["Type"]);
        Assert.Equal("true", entitlement["Value"]);
        Assert.False(result.HasDiagnostic("warning", "CARKIT001"));
    }

    [Fact]
    public void Iot_on_ios_warns_CARKIT001()
    {
        var result = Probe(Ios, ConfigureIos, "Parking;Iot");

        Assert.Equal(0, result.ExitCode);
        Assert.True(result.HasDiagnostic("warning", "CARKIT001"), result.Log);
        Assert.Contains("Iot", result.Log, StringComparison.Ordinal);
        Assert.Equal(["com.apple.developer.carplay-parking"], result.Identities("CustomEntitlements"));
    }

    [Fact]
    public void Quick_food_ordering_on_android_warns_CARKIT001()
    {
        var result = Probe(Android, ConfigureAndroid, "QuickFoodOrdering;Parking");

        Assert.Equal(0, result.ExitCode);
        Assert.True(result.HasDiagnostic("warning", "CARKIT001"), result.Log);
        Assert.Contains("QuickFoodOrdering", result.Log, StringComparison.Ordinal);
        Assert.Equal(["androidx.car.app.category.POI"], result.Identities("CarKitAndroidIntentCategory"));
    }

    // Stage 7: the voice/video/public safety keys verified in the Apple Developer Documentation, CARKIT003 withdrawn
    [Fact]
    public void Verified_entitlements_do_not_warn_carkit003()
    {
        var result = Probe(Ios, ConfigureIos, "VoiceConversational;DrivingTask;Video;PublicSafety");

        Assert.Equal(0, result.ExitCode);
        Assert.False(result.HasDiagnostic("warning", "CARKIT003"), result.Log);
        Assert.False(result.HasDiagnostic("warning", "CARKIT001"));
        Assert.Equal(
            ["com.apple.developer.carplay-driving-task", "com.apple.developer.carplay-public-safety", "com.apple.developer.carplay-video", "com.apple.developer.carplay-voice-based-conversation"],
            result.Identities("CustomEntitlements").Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("Audio", "com.apple.developer.carplay-audio")]
    [InlineData("Communication", "com.apple.developer.carplay-communication")]
    [InlineData("Navigation", "com.apple.developer.carplay-maps")]
    [InlineData("Parking", "com.apple.developer.carplay-parking")]
    [InlineData("EvCharging", "com.apple.developer.carplay-charging")]
    [InlineData("Fueling", "com.apple.developer.carplay-fueling")]
    [InlineData("QuickFoodOrdering", "com.apple.developer.carplay-quick-ordering")]
    [InlineData("DrivingTask", "com.apple.developer.carplay-driving-task")]
    [InlineData("VoiceConversational", "com.apple.developer.carplay-voice-based-conversation")]
    [InlineData("Video", "com.apple.developer.carplay-video")]
    [InlineData("PublicSafety", "com.apple.developer.carplay-public-safety")]
    public void Every_carplay_category_adds_its_entitlement(string category, string entitlement)
    {
        var result = Probe(Ios, ConfigureIos, category, new() { ["RuntimeIdentifier"] = "ios-arm64" });

        Assert.Equal(0, result.ExitCode);
        var item = result["CustomEntitlements"].Single(e => e.Identity.StartsWith("com.apple.developer.carplay-", StringComparison.Ordinal));
        Assert.Equal((entitlement, "Boolean", "true"), (item.Identity, item["Type"], item["Value"]));
        Assert.False(result.HasDiagnostic("warning", "CARKIT001"), result.Log);
        Assert.False(result.HasDiagnostic("warning", "CARKIT003"), result.Log);
    }

    [Theory]
    [InlineData("QuickFoodOrdering")]
    [InlineData("DrivingTask")]
    [InlineData("VoiceConversational")]
    [InlineData("Video")]
    [InlineData("PublicSafety")]
    public void Ios_only_categories_warn_carkit001_on_android(string category)
    {
        var result = Probe(Android, ConfigureAndroid, category);

        Assert.Equal(0, result.ExitCode);
        Assert.True(result.HasDiagnostic("warning", "CARKIT001"), result.Log);
        Assert.Contains(category, result.Log, StringComparison.Ordinal);
        Assert.Empty(result["CarKitAndroidIntentCategory"]);
        Assert.Empty(result["CarKitAutomotiveUses"]);
    }

    [Theory]
    [InlineData("Iot")]
    [InlineData("Weather")]
    public void Android_only_categories_warn_carkit001_on_ios(string category)
    {
        var result = Probe(Ios, ConfigureIos, category, new() { ["RuntimeIdentifier"] = "ios-arm64" });

        Assert.Equal(0, result.ExitCode);
        Assert.True(result.HasDiagnostic("warning", "CARKIT001"), result.Log);
        Assert.Empty(result["CustomEntitlements"]);
    }

    [Theory]
    [InlineData("Iot", "androidx.car.app.category.IOT")]
    [InlineData("Weather", "androidx.car.app.category.WEATHER")]
    public void Iot_and_weather_declare_their_intent_categories_without_map_permission(string category, string intentCategory)
    {
        var result = Probe(Android, ConfigureAndroid, category);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal([intentCategory], result.Identities("CarKitAndroidIntentCategory"));
        Assert.Empty(result["CarKitAndroidPermission"]);
        Assert.Equal(["template"], result.Identities("CarKitAutomotiveUses"));
        Assert.Equal("true", result.Property("CarKitAndroidGenerateService"));
        Assert.False(result.HasDiagnostic("warning", "CARKIT001"), result.Log);
    }

    // Google: "Don't include both the MAP_TEMPLATES and the NAVIGATION_TEMPLATES permissions … or your app will be
    // rejected"
    [Fact]
    public void Poi_with_navigation_declares_only_navigation_templates()
    {
        var result = Probe(Android, ConfigureAndroid, "Parking;Navigation;Weather");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(
            ["androidx.car.app.category.NAVIGATION", "androidx.car.app.category.POI", "androidx.car.app.category.WEATHER"],
            result.Identities("CarKitAndroidIntentCategory").Order(StringComparer.Ordinal));
        Assert.Equal(
            ["androidx.car.app.ACCESS_SURFACE", "androidx.car.app.NAVIGATION_TEMPLATES"],
            result.Identities("CarKitAndroidPermission").Order(StringComparer.Ordinal));
        Assert.Equal("androidx.car.app.NAVIGATION_TEMPLATES,androidx.car.app.ACCESS_SURFACE", result.Property("CarKitAndroidPermissions"));
    }

    [Fact]
    public void Ios_voice_conversational_adds_a_microphone_usage_description()
    {
        var defaulted = Probe(Ios, ConfigureIos, "VoiceConversational");
        Assert.Equal(0, defaulted.ExitCode);
        Assert.Equal(2, defaulted["PartialAppManifest"].Count);
        Assert.Contains("<key>NSMicrophoneUsageDescription</key>\n  <string>The voice assistant listens to your questions while you drive.</string>",
            defaulted.ReadFile("obj/carkit/CarKitMicrophone.plist"), StringComparison.Ordinal);

        var custom = Probe(Ios, ConfigureIos, "VoiceConversational", new() { ["CarKitIosMicrophoneUsageDescription"] = "Assistant <voice> & microphone" });
        Assert.Contains("<string>Assistant &lt;voice&gt; &amp; microphone</string>", custom.ReadFile("obj/carkit/CarKitMicrophone.plist"), StringComparison.Ordinal);

        var plist = Path.Combine(Path.GetTempPath(), "carkit-" + Guid.NewGuid().ToString("N") + "-Info.plist");
        File.WriteAllText(plist, "<plist version=\"1.0\"><dict>\n<key>NSMicrophoneUsageDescription</key>\n<string>Custom description</string>\n</dict></plist>");
        try
        {
            var kept = Probe(Ios, ConfigureIos, "VoiceConversational", new() { ["AppBundleManifest"] = plist });
            Assert.EndsWith("CarKitScenes.plist", Assert.Single(kept["PartialAppManifest"])["FullPath"], StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(plist);
        }

        Assert.Single(Probe(Ios, ConfigureIos, "Parking")["PartialAppManifest"]);
    }

    [Fact]
    public void Unknown_category_fails_with_CARKIT002()
    {
        var result = Probe(Ios, ConfigureIos, "Parking;Poi");

        Assert.NotEqual(0, result.ExitCode);
        Assert.True(result.HasDiagnostic("error", "CARKIT002"), result.Log);
        Assert.Contains("Poi", result.Log, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ; ")]
    public void Missing_categories_fail_with_CARKIT002(string? categories)
    {
        var result = Probe(Android, ConfigureAndroid, categories);

        Assert.NotEqual(0, result.ExitCode);
        Assert.True(result.HasDiagnostic("error", "CARKIT002"), result.Log);
        Assert.Contains("CarKitCategories", result.Log, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("9")]
    [InlineData("abc")]
    public void Invalid_min_car_api_level_fails_with_CARKIT002(string level)
    {
        var result = Probe(Android, ConfigureAndroid, "Parking", new() { ["CarKitAndroidMinCarApiLevel"] = level });

        Assert.NotEqual(0, result.ExitCode);
        Assert.True(result.HasDiagnostic("error", "CARKIT002"), result.Log);
    }

    // Stage 9: the upper bound is the highest API level of the Car App binding (1.7.0.1 → 8); binding 1.9 will raise it
    // to 9
    [Fact]
    public void Max_car_api_level_follows_the_binding()
    {
        var current = Probe(Android, ConfigureAndroid, "Parking", new() { ["CarKitAndroidMinCarApiLevel"] = "9" });
        var raised = Probe(Android, ConfigureAndroid, "Parking", new() { ["CarKitAndroidMinCarApiLevel"] = "9", ["_CarKitMaxCarApiLevel"] = "9" });

        Assert.Contains("from 1 to 8", current.Log, StringComparison.Ordinal);
        Assert.False(raised.HasDiagnostic("error", "CARKIT002"), raised.Log);
        Assert.Equal("9", raised.Property("CarKitAndroidMinCarApiLevel"));
    }

    // Stage 8: the CarPlay dashboard and cluster work with the Navigation module (CARKIT004 lifted, no module → CARKIT009)
    [Theory]
    [InlineData(Android, ConfigureAndroid, "CarKitEnableAutomotive")]
    public void Unavailable_features_fail_with_CARKIT004(string platform, string target, string property)
    {
        var result = Probe(platform, target, "Navigation", new() { [property] = "true" });

        Assert.NotEqual(0, result.ExitCode);
        Assert.True(result.HasDiagnostic("error", "CARKIT004"), result.Log);
    }

    [Theory]
    [InlineData(Ios, ConfigureIos)]
    [InlineData(Android, ConfigureAndroid)]
    public void Library_projects_are_ignored(string platform, string target)
    {
        var result = MSBuildProbe.Run(platform, target, new Dictionary<string, string>(), app: false);

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result["CustomEntitlements"]);
        Assert.Empty(result["PartialAppManifest"]);
        Assert.Empty(result["AndroidResource"]);
        Assert.Empty(result["RuntimeHostConfigurationOption"]);
        Assert.Equal(string.Empty, result.Property("CarKitAndroidGenerateService"));
    }

    [Fact]
    public void Poi_categories_share_one_intent_category()
    {
        var result = Probe(Android, ConfigureAndroid, "Parking;Fueling;EvCharging");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(["androidx.car.app.category.POI"], result.Identities("CarKitAndroidIntentCategory"));
        Assert.Equal(["androidx.car.app.MAP_TEMPLATES"], result.Identities("CarKitAndroidPermission"));
        Assert.Equal("true", result.Property("CarKitAndroidGenerateService"));
        Assert.Equal("androidx.car.app.category.POI", result.Property("CarKitAndroidIntentCategories"));
        Assert.Equal("androidx.car.app.MAP_TEMPLATES", result.Property("CarKitAndroidPermissions"));
        Assert.Equal("1", result.Property("CarKitAndroidMinCarApiLevel"));
    }

    [Fact]
    public void Communication_and_navigation_map_to_their_intent_categories_and_permissions()
    {
        var result = Probe(Android, ConfigureAndroid, "Communication;Navigation", new() { ["CarKitAndroidMinCarApiLevel"] = "6" });

        Assert.Equal(0, result.ExitCode);
        // Stage 6: CALLING only with the Core-Telecom binding (CarKitAndroidCalling → CARKIT004)
        Assert.Equal(
            ["androidx.car.app.category.MESSAGING", "androidx.car.app.category.NAVIGATION"],
            result.Identities("CarKitAndroidIntentCategory").Order(StringComparer.Ordinal));
        Assert.Equal(
            ["androidx.car.app.ACCESS_SURFACE", "androidx.car.app.NAVIGATION_TEMPLATES"],
            result.Identities("CarKitAndroidPermission").Order(StringComparer.Ordinal));
        Assert.Equal("6", result.Property("CarKitAndroidMinCarApiLevel"));
    }

    [Fact]
    public void Audio_on_android_uses_media_descriptor_without_service()
    {
        var result = Probe(Android, ConfigureAndroid, "Audio");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result["CarKitAndroidIntentCategory"]);
        Assert.Equal(["media"], result.Identities("CarKitAutomotiveUses"));
        Assert.Equal("true", result.Property("CarKitAndroidAutomotiveDescriptor"));
        Assert.False(result.HasDiagnostic("warning", "CARKIT001"));
    }

    [Fact]
    public void Automotive_descriptor_lists_template_and_media()
    {
        var result = Probe(Android, ConfigureAndroid, "Parking;Audio");

        Assert.Equal(0, result.ExitCode);
        var resource = Assert.Single(result["AndroidResource"]);
        Assert.Equal("xml/automotive_app_desc.xml", resource["LogicalName"]);
        var xml = result.ReadFile("obj/carkit/res/xml/automotive_app_desc.xml");
        Assert.Contains("<automotiveApp>", xml, StringComparison.Ordinal);
        Assert.Contains("<uses name=\"template\" />", xml, StringComparison.Ordinal);
        Assert.Contains("<uses name=\"media\" />", xml, StringComparison.Ordinal);
    }

    // Stage 5 (found in a device test): Roslyn reads CompilerVisibleProperty from .editorconfig, where ';' and '#'
    // start a comment, so the list "a;b" reaches the generator as "a". Lists for the generator are comma-separated.
    [Fact]
    public void Generator_lists_survive_the_editorconfig_format()
    {
        var result = Probe(Android, ConfigureAndroid, "Communication;Navigation;Audio", new() { ["CarKitMediaModule"] = "true" });

        Assert.Equal(0, result.ExitCode);
        foreach (var name in new[] { "CarKitAndroidIntentCategories", "CarKitAndroidPermissions", "CarKitAndroidMediaPermissions" })
        {
            Assert.DoesNotContain(';', result.Property(name));
            Assert.DoesNotContain('#', result.Property(name));
        }

        Assert.Equal(
            "androidx.car.app.category.MESSAGING,androidx.car.app.category.NAVIGATION",
            result.Property("CarKitAndroidIntentCategories"));
        Assert.Equal("android.permission.FOREGROUND_SERVICE,android.permission.FOREGROUND_SERVICE_MEDIA_PLAYBACK", result.Property("CarKitAndroidMediaPermissions"));
    }

    // Stage 5: Audio + Media module (package props: CarKitMediaModule) → media service and foreground permissions
    [Fact]
    public void Audio_with_the_media_module_generates_the_media_service()
    {
        var result = Probe(Android, ConfigureAndroid, "Audio", new() { ["CarKitMediaModule"] = "true" });

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("true", result.Property("CarKitAndroidMediaService"));
        Assert.Equal("android.permission.FOREGROUND_SERVICE,android.permission.FOREGROUND_SERVICE_MEDIA_PLAYBACK", result.Property("CarKitAndroidMediaPermissions"));
        Assert.Empty(result["CarKitAndroidIntentCategory"]);
        Assert.Contains("CarKitAndroidMediaService", result.Identities("CompilerVisibleProperty"));
        Assert.Contains("CarKitAndroidMediaPermissions", result.Identities("CompilerVisibleProperty"));
        Assert.False(result.HasDiagnostic("warning", "CARKIT005"));
    }

    [Theory]
    [InlineData(Android, ConfigureAndroid)]
    [InlineData(Ios, ConfigureIos)]
    public void Audio_without_the_media_module_warns_carkit005(string platform, string target)
    {
        var result = Probe(platform, target, "Audio;Parking");

        Assert.Equal(0, result.ExitCode);
        Assert.True(result.HasDiagnostic("warning", "CARKIT005"), result.Log);
        Assert.Contains("CarKit.Maui.Media", result.Log, StringComparison.Ordinal);
        Assert.NotEqual("true", result.Property("CarKitAndroidMediaService"));
    }

    [Fact]
    public void Templated_media_adds_the_media_category_and_permission()
    {
        var result = Probe(Android, ConfigureAndroid, "Audio", new() { ["CarKitMediaModule"] = "true", ["CarKitAndroidTemplatedMedia"] = "true" });

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(["androidx.car.app.category.MEDIA"], result.Identities("CarKitAndroidIntentCategory"));
        Assert.Equal(["androidx.car.app.MEDIA_TEMPLATES"], result.Identities("CarKitAndroidPermission"));
        Assert.Equal(["media", "template"], result.Identities("CarKitAutomotiveUses").Order(StringComparer.Ordinal));
        Assert.Equal("true", result["RuntimeHostConfigurationOption"].Single(o => o.Identity == "CarKit.Maui.Media.TemplatedMedia")["Value"]);
        Assert.Equal("true", result.Property("CarKitAndroidMediaService"));
    }

    [Fact]
    public void Ios_audio_adds_the_background_mode()
    {
        var result = Probe(Ios, ConfigureIos, "Audio", new() { ["CarKitMediaModule"] = "true" });

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(2, result["PartialAppManifest"].Count);
        var plist = result.ReadFile("obj/carkit/CarKitBackgroundAudio.plist");
        Assert.Contains("<key>UIBackgroundModes</key>\n  <array>\n    <string>audio</string>", plist, StringComparison.Ordinal);

        var disabled = Probe(Ios, ConfigureIos, "Audio", new() { ["CarKitMediaModule"] = "true", ["CarKitIosBackgroundAudio"] = "false" });
        Assert.EndsWith("CarKitScenes.plist", Assert.Single(disabled["PartialAppManifest"])["FullPath"], StringComparison.Ordinal);

        var parking = Probe(Ios, ConfigureIos, "Parking");
        Assert.Single(parking["PartialAppManifest"]);
    }

    // Review M15 (package smoke test): the iOS SDK overwrites arrays from PartialAppManifest, so the app's
    // UIBackgroundModes (e.g. fetch) would vanish; when the app has its own array CarKit leaves it alone, and warns
    // with CARKIT006 if "audio" is missing from it
    [Theory]
    [InlineData("fetch", true)]
    [InlineData("audio", false)]
    public void App_background_modes_are_kept(string mode, bool warns)
    {
        var plist = Path.Combine(Path.GetTempPath(), "carkit-" + Guid.NewGuid().ToString("N") + "-Info.plist");
        File.WriteAllText(plist, $"<plist version=\"1.0\"><dict>\n<key>UIBackgroundModes</key>\n<array>\n<string>{mode}</string>\n</array>\n<key>NSUsage</key><string>Don't break quoting</string>\n</dict></plist>");
        try
        {
            var result = Probe(Ios, ConfigureIos, "Audio", new() { ["CarKitMediaModule"] = "true", ["AppBundleManifest"] = plist });

            Assert.Equal(0, result.ExitCode);
            Assert.EndsWith("CarKitScenes.plist", Assert.Single(result["PartialAppManifest"])["FullPath"], StringComparison.Ordinal);
            Assert.Equal(warns, result.HasDiagnostic("warning", "CARKIT006"));
        }
        finally
        {
            File.Delete(plist);
        }
    }

    // Found in CI: a comment in the sample Info.plist ("UIBackgroundModes = audio is added by CarKit") counted as the
    // app's array
    [Fact]
    public void Background_modes_in_a_comment_do_not_count()
    {
        var plist = Path.Combine(Path.GetTempPath(), "carkit-" + Guid.NewGuid().ToString("N") + "-Info.plist");
        File.WriteAllText(plist, "<plist version=\"1.0\"><dict>\n<!-- UIBackgroundModes = audio is added by CarKit -->\n</dict></plist>");
        try
        {
            var result = Probe(Ios, ConfigureIos, "Audio", new() { ["CarKitMediaModule"] = "true", ["AppBundleManifest"] = plist });

            Assert.Equal(0, result.ExitCode);
            Assert.Equal(2, result["PartialAppManifest"].Count);
            Assert.False(result.HasDiagnostic("warning", "CARKIT006"));
        }
        finally
        {
            File.Delete(plist);
        }
    }

    [Fact]
    public void Android_generator_inputs_are_compiler_visible()
    {
        var result = Probe(Android, ConfigureAndroid, "Parking");

        Assert.Superset(
            new HashSet<string>(["CarKitAndroidGenerateService", "CarKitAndroidIntentCategories", "CarKitAndroidPermissions", "CarKitAndroidMinCarApiLevel", "CarKitAndroidAutomotiveDescriptor"]),
            result.Identities("CompilerVisibleProperty").ToHashSet());
    }

    [Theory]
    [InlineData("Release", "Warn")]
    [InlineData("Debug", null)]
    public void Release_defaults_validation_to_warn(string configuration, string? expected)
    {
        var result = Probe(Ios, ConfigureIos, "Parking", new() { ["Configuration"] = configuration });

        var option = result["RuntimeHostConfigurationOption"].SingleOrDefault(o => o.Identity == "CarKit.Maui.Validation");
        Assert.Equal(expected, option?["Value"]);
    }

    [Fact]
    public void Explicit_validation_wins_in_release()
    {
        var result = Probe(Android, ConfigureAndroid, "Parking", new() { ["Configuration"] = "Release", ["CarKitValidation"] = "Strict" });

        Assert.Equal("Strict", result["RuntimeHostConfigurationOption"].Single(o => o.Identity == "CarKit.Maui.Validation")["Value"]);
    }

    [Fact]
    public void Categories_flow_to_runtime_switch()
    {
        var result = Probe(Android, ConfigureAndroid, "Parking;Audio");

        Assert.Equal("Parking;Audio", result["RuntimeHostConfigurationOption"].Single(o => o.Identity == "CarKit.Maui.Categories")["Value"]);
    }

    [Fact]
    public void Android_apps_get_runtime_switches_although_the_sdk_sets_library_output()
    {
        // Microsoft.Android.Sdk turns OutputType=Exe into Library and sets AndroidApplication=true
        var result = MSBuildProbe.Run(Android, ConfigureAndroid, new Dictionary<string, string>
        {
            ["OutputType"] = "Library",
            ["AndroidApplication"] = "true",
            ["CarKitCategories"] = "Parking",
            ["Configuration"] = "Release",
        }, app: false);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("Parking", result["RuntimeHostConfigurationOption"].Single(o => o.Identity == "CarKit.Maui.Categories")["Value"]);
        Assert.Equal("Warn", result["RuntimeHostConfigurationOption"].Single(o => o.Identity == "CarKit.Maui.Validation")["Value"]);
        Assert.Equal("true", result.Property("CarKitAndroidGenerateService"));
    }

    [Fact]
    public void Scene_manifest_uses_maui_configuration_for_both_roles()
    {
        var result = Probe(Ios, ConfigureIos, "Parking");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("CarKitConfigureIos", result.Property("CollectAppManifestsDependsOn"), StringComparison.Ordinal);
        var manifest = Assert.Single(result["PartialAppManifest"]);
        Assert.EndsWith("obj/carkit/CarKitScenes.plist", manifest["FullPath"], StringComparison.Ordinal);
        var plist = result.ReadFile("obj/carkit/CarKitScenes.plist");
        Assert.Contains("<key>UIApplicationSceneManifest</key>", plist, StringComparison.Ordinal);
        Assert.Contains("<key>UIApplicationSupportsMultipleScenes</key>\n    <true/>", plist, StringComparison.Ordinal);
        Assert.Contains("<key>UIWindowSceneSessionRoleApplication</key>", plist, StringComparison.Ordinal);
        Assert.Contains("<key>CPTemplateApplicationSceneSessionRoleApplication</key>", plist, StringComparison.Ordinal);
        Assert.Contains("<string>CPTemplateApplicationScene</string>", plist, StringComparison.Ordinal);
        Assert.Contains("<string>CarKitSceneDelegate</string>", plist, StringComparison.Ordinal);
        Assert.Contains("<string>CarKitPhoneSceneDelegate</string>", plist, StringComparison.Ordinal);
        Assert.Equal(2, plist.Split("<string>__MAUI_DEFAULT_SCENE_CONFIGURATION__</string>").Length - 1);
    }

    [Fact]
    public void Custom_phone_scene_delegate_is_used()
    {
        var result = Probe(Ios, ConfigureIos, "Parking", new() { ["CarKitIosPhoneSceneDelegate"] = "SceneDelegate" });

        var plist = result.ReadFile("obj/carkit/CarKitScenes.plist");
        Assert.Contains("<string>SceneDelegate</string>", plist, StringComparison.Ordinal);
        Assert.DoesNotContain("CarKitPhoneSceneDelegate", plist, StringComparison.Ordinal);
    }

    [Fact]
    public void Scene_manifest_and_entitlements_can_be_disabled()
    {
        var result = Probe(Ios, ConfigureIos, "Parking", new() { ["CarKitIosGenerateSceneManifest"] = "false", ["CarKitIosEntitlements"] = "false" });

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result["PartialAppManifest"]);
        Assert.Empty(result["CustomEntitlements"]);
    }

    // Stage 10: the CarPlay Simulator window shows only apps with a category key in the (simulated) entitlements
    [Theory]
    [InlineData("iossimulator-arm64", null, true)]
    [InlineData("ios-arm64", null, true)]
    [InlineData("iossimulator-arm64", "false", false)]
    [InlineData("ios-arm64", "false", false)]
    public void CarPlay_entitlements_are_in_device_and_simulator_builds(string runtimeIdentifier, string? entitlements, bool expected)
    {
        var properties = new Dictionary<string, string> { ["RuntimeIdentifier"] = runtimeIdentifier };
        if (entitlements is not null)
        {
            properties["CarKitIosEntitlements"] = entitlements;
        }

        var result = Probe(Ios, ConfigureIos, "Parking", properties);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(expected, result.Identities("CustomEntitlements").Contains("com.apple.developer.carplay-parking"));
        Assert.Single(result["PartialAppManifest"]);
    }

    // Stage 10: a simulator build without a provisioning profile, and the CarPlay host (CarPlayTemplateUIHost) rejects
    // the app's connection when application-identifier has no team prefix made of [0-9A-Z]
    // ("team prefix must only contain characters…")
    [Fact]
    public void Simulator_build_is_prepared_for_the_carplay_window()
    {
        var result = Probe(Ios, ConfigureIos, "Parking", new() { ["RuntimeIdentifier"] = "iossimulator-arm64", ["ApplicationId"] = "com.example.parking" });

        Assert.Equal(0, result.ExitCode);
        var identifier = result["CustomEntitlements"].Single(e => e.Identity == "application-identifier");
        Assert.Equal(("String", "FAKETEAMID.com.example.parking"), (identifier["Type"], identifier["Value"]));
        Assert.Equal("false", result.Property("CodesignRequireProvisioningProfile"));
    }

    [Fact]
    public void Simulator_preparation_respects_the_app()
    {
        var result = Probe(Ios, ConfigureIos, "Parking", new()
        {
            ["RuntimeIdentifier"] = "iossimulator-arm64",
            ["ApplicationId"] = "com.example.parking",
            ["CodesignRequireProvisioningProfile"] = "true",
        });

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("true", result.Property("CodesignRequireProvisioningProfile"));
    }

    [Fact]
    public void Device_build_keeps_the_signing_of_the_provisioning_profile()
    {
        var result = Probe(Ios, ConfigureIos, "Parking", new() { ["RuntimeIdentifier"] = "ios-arm64", ["ApplicationId"] = "com.example.parking" });

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("application-identifier", result.Identities("CustomEntitlements"));
        Assert.Equal(string.Empty, result.Property("CodesignRequireProvisioningProfile"));
    }

    [Fact]
    public void Simulator_build_without_entitlements_is_not_touched()
    {
        var result = Probe(Ios, ConfigureIos, "Parking", new()
        {
            ["RuntimeIdentifier"] = "iossimulator-arm64",
            ["ApplicationId"] = "com.example.parking",
            ["CarKitIosEntitlements"] = "false",
        });

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result["CustomEntitlements"]);
        Assert.Equal(string.Empty, result.Property("CodesignRequireProvisioningProfile"));
    }

    [Fact]
    public void Normalizes_category_formatting()
    {
        var result = Probe(Ios, ConfigureIos, " parking , Audio ");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(
            ["com.apple.developer.carplay-audio", "com.apple.developer.carplay-parking"],
            result.Identities("CustomEntitlements").Order(StringComparer.Ordinal));
        Assert.Equal("Audio;Parking", result.Property("CarKitNormalizedCategories"));
        Assert.Equal("parking;Audio", result["RuntimeHostConfigurationOption"].Single(o => o.Identity == "CarKit.Maui.Categories")["Value"]);
    }

    // Stage 6 (spec: "Communication module decisions (Stage 6)", Build and the Android call gate)
    [Fact]
    public void Communication_declares_messaging_without_calling()
    {
        var result = Probe(Android, ConfigureAndroid, "Communication", new() { ["CarKitCommunicationModule"] = "true" });

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(["androidx.car.app.category.MESSAGING"], result.Identities("CarKitAndroidIntentCategory"));
        Assert.Equal("androidx.car.app.category.MESSAGING", result.Property("CarKitAndroidIntentCategories"));
        Assert.DoesNotContain("CALLING", result.ReadFile("obj/carkit/res/xml/automotive_app_desc.xml"), StringComparison.Ordinal);
    }

    [Fact]
    public void Communication_with_the_module_uses_notification_and_template()
    {
        var result = Probe(Android, ConfigureAndroid, "Communication", new() { ["CarKitCommunicationModule"] = "true" });

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(["notification", "template"], result.Identities("CarKitAutomotiveUses").Order(StringComparer.Ordinal));
        var descriptor = result.ReadFile("obj/carkit/res/xml/automotive_app_desc.xml");
        Assert.Contains("<uses name=\"template\" />", descriptor, StringComparison.Ordinal);
        Assert.Contains("<uses name=\"notification\" />", descriptor, StringComparison.Ordinal);
        Assert.Equal("true", result.Property("CarKitAndroidMessageReceiver"));
        Assert.Equal("android.permission.POST_NOTIFICATIONS", result.Property("CarKitAndroidMessagePermissions"));
        Assert.False(result.HasDiagnostic("warning", "CARKIT007"));
    }

    [Theory]
    [InlineData(Ios, ConfigureIos)]
    [InlineData(Android, ConfigureAndroid)]
    public void Communication_without_the_module_warns_carkit007(string platform, string target)
    {
        var result = Probe(platform, target, "Communication");

        Assert.Equal(0, result.ExitCode);
        Assert.True(result.HasDiagnostic("warning", "CARKIT007"), result.Log);
        Assert.Equal(string.Empty, result.Property("CarKitAndroidMessageReceiver"));
        Assert.DoesNotContain("notification", result.Identities("CarKitAutomotiveUses"));
        Assert.False(Probe(platform, target, "Parking").HasDiagnostic("warning", "CARKIT007"));
    }

    [Fact]
    public void Android_calling_is_gated_with_carkit004()
    {
        var result = Probe(Android, ConfigureAndroid, "Communication", new() { ["CarKitCommunicationModule"] = "true", ["CarKitAndroidCalling"] = "true" });

        Assert.NotEqual(0, result.ExitCode);
        Assert.True(result.HasDiagnostic("error", "CARKIT004"), result.Log);
        Assert.Contains("Core-Telecom", result.Log, StringComparison.Ordinal);
        Assert.Equal(0, Probe(Android, ConfigureAndroid, "Communication", new() { ["CarKitAndroidCalling"] = "false" }).ExitCode);
    }

    [Fact]
    public void Ios_communication_adds_siri_and_app_group_on_device_builds()
    {
        var properties = new Dictionary<string, string>
        {
            ["CarKitCommunicationModule"] = "true",
            ["CarKitIosAppGroup"] = "group.com.carkit.sample",
            ["RuntimeIdentifier"] = "ios-arm64",
        };

        var device = Probe(Ios, ConfigureIos, "Communication", properties);

        Assert.Equal(0, device.ExitCode);
        Assert.Equal(
            ["com.apple.developer.carplay-communication", "com.apple.developer.siri", "com.apple.security.application-groups"],
            device.Identities("CustomEntitlements").Order(StringComparer.Ordinal));
        var siri = device["CustomEntitlements"].Single(e => e.Identity == "com.apple.developer.siri");
        Assert.Equal(("Boolean", "true"), (siri["Type"], siri["Value"]));
        var group = device["CustomEntitlements"].Single(e => e.Identity == "com.apple.security.application-groups");
        Assert.Equal(("StringArray", "group.com.carkit.sample"), (group["Type"], group["Value"]));
        Assert.Equal("group.com.carkit.sample", device["RuntimeHostConfigurationOption"].Single(o => o.Identity == "CarKit.Maui.Communication.AppGroup")["Value"]);

        properties["RuntimeIdentifier"] = "iossimulator-arm64";
        var simulator = Probe(Ios, ConfigureIos, "Communication", properties);
        Assert.Equal(["com.apple.developer.carplay-communication"], simulator.Identities("CustomEntitlements").Where(e => e.StartsWith("com.apple.", StringComparison.Ordinal)));
        Assert.Contains("CarKit.Maui.Communication.AppGroup", simulator.Identities("RuntimeHostConfigurationOption"));

        // Without the module: only the CarPlay entitlement (the module handles Siri and App Group)
        properties["RuntimeIdentifier"] = "ios-arm64";
        properties.Remove("CarKitCommunicationModule");
        Assert.Equal(["com.apple.developer.carplay-communication"], Probe(Ios, ConfigureIos, "Communication", properties).Identities("CustomEntitlements"));
    }

    [Fact]
    public void Ios_siri_usage_description_is_kept_or_defaulted()
    {
        var module = new Dictionary<string, string> { ["CarKitCommunicationModule"] = "true" };
        var defaulted = Probe(Ios, ConfigureIos, "Communication", module);
        Assert.Equal(0, defaulted.ExitCode);
        Assert.Equal(2, defaulted["PartialAppManifest"].Count);
        Assert.Contains("<key>NSSiriUsageDescription</key>\n  <string>Siri reads your messages aloud and sends your replies while you drive.</string>",
            defaulted.ReadFile("obj/carkit/CarKitSiri.plist"), StringComparison.Ordinal);

        // Review M6: a semicolon in the app's text does not split the file into lines (the property function result
        // has ; as %3B)
        var custom = Probe(Ios, ConfigureIos, "Communication", new(module) { ["CarKitIosSiriUsageDescription"] = "Siri & messages <car>; replies" });
        Assert.Contains("<string>Siri &amp; messages &lt;car&gt;; replies</string>", custom.ReadFile("obj/carkit/CarKitSiri.plist"), StringComparison.Ordinal);

        var plist = Path.Combine(Path.GetTempPath(), "carkit-" + Guid.NewGuid().ToString("N") + "-Info.plist");
        File.WriteAllText(plist, "<plist version=\"1.0\"><dict>\n<key>NSSiriUsageDescription</key>\n<string>Custom description</string>\n</dict></plist>");
        try
        {
            var kept = Probe(Ios, ConfigureIos, "Communication", new(module) { ["AppBundleManifest"] = plist });
            Assert.EndsWith("CarKitScenes.plist", Assert.Single(kept["PartialAppManifest"])["FullPath"], StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(plist);
        }

        Assert.Single(Probe(Ios, ConfigureIos, "Communication")["PartialAppManifest"]);
    }

    // Stage 8: Navigation module (spec: "Navigation module decisions (Stage 8)", Build and iOS/Android hosting)
    [Theory]
    [InlineData(Ios, ConfigureIos)]
    [InlineData(Android, ConfigureAndroid)]
    public void Navigation_without_module_warns_carkit008(string platform, string target)
    {
        var result = Probe(platform, target, "Navigation");

        Assert.Equal(0, result.ExitCode);
        Assert.True(result.HasDiagnostic("warning", "CARKIT008"), result.Log);
        Assert.False(Probe(platform, target, "Navigation", Navigation()).HasDiagnostic("warning", "CARKIT008"));
        Assert.False(Probe(platform, target, "Parking").HasDiagnostic("warning", "CARKIT008"));
    }

    // I6 (review): voice prompts and maneuver updates while the phone is locked require UIBackgroundModes = audio
    // (like Audio, Stage 5); location is an app decision (the position source), so CarKit does not add it
    [Fact]
    public void Ios_navigation_adds_the_background_audio_mode()
    {
        var result = Probe(Ios, ConfigureIos, "Navigation", Navigation());

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(2, result["PartialAppManifest"].Count);
        var plist = result.ReadFile("obj/carkit/CarKitBackgroundAudio.plist");
        Assert.Contains("<string>audio</string>", plist, StringComparison.Ordinal);
        Assert.DoesNotContain("location", plist, StringComparison.Ordinal);

        Assert.Single(Probe(Ios, ConfigureIos, "Navigation", Navigation(new() { ["CarKitIosBackgroundAudio"] = "false" }))["PartialAppManifest"]);
        Assert.Single(Probe(Ios, ConfigureIos, "Navigation")["PartialAppManifest"]);
    }

    [Fact]
    public void Dashboard_and_cluster_add_scene_roles_and_keys()
    {
        var plain = Probe(Ios, ConfigureIos, "Navigation", Navigation());
        var plainPlist = plain.ReadFile("obj/carkit/CarKitScenes.plist");
        Assert.DoesNotContain("Dashboard", plainPlist, StringComparison.Ordinal);
        Assert.DoesNotContain("InstrumentCluster", plainPlist, StringComparison.Ordinal);

        var result = Probe(Ios, ConfigureIos, "Navigation", Navigation(new() { ["CarKitIosDashboard"] = "true", ["CarKitIosInstrumentCluster"] = "true" }));

        Assert.Equal(0, result.ExitCode);
        Assert.False(result.HasDiagnostic("error", "CARKIT004"), result.Log);
        var plist = result.ReadFile("obj/carkit/CarKitScenes.plist");
        Assert.Contains("<key>CPSupportsDashboardNavigationScene</key>\n    <true/>", plist, StringComparison.Ordinal);
        Assert.Contains("<key>CPSupportsInstrumentClusterNavigationScene</key>\n    <true/>", plist, StringComparison.Ordinal);
        Assert.Contains("<key>CPTemplateApplicationDashboardSceneSessionRoleApplication</key>", plist, StringComparison.Ordinal);
        Assert.Contains("<string>CPTemplateApplicationDashboardScene</string>", plist, StringComparison.Ordinal);
        Assert.Contains("<string>CarKitDashboardSceneDelegate</string>", plist, StringComparison.Ordinal);
        Assert.Contains("<key>CPTemplateApplicationInstrumentClusterSceneSessionRoleApplication</key>", plist, StringComparison.Ordinal);
        Assert.Contains("<string>CPTemplateApplicationInstrumentClusterScene</string>", plist, StringComparison.Ordinal);
        Assert.Contains("<string>CarKitInstrumentClusterSceneDelegate</string>", plist, StringComparison.Ordinal);
        // MauiUIApplicationDelegate.GetConfiguration returns this name for every role
        Assert.Equal(4, plist.Split("<string>__MAUI_DEFAULT_SCENE_CONFIGURATION__</string>").Length - 1);

        var dashboardOnly = Probe(Ios, ConfigureIos, "Navigation", Navigation(new() { ["CarKitIosDashboard"] = "true" })).ReadFile("obj/carkit/CarKitScenes.plist");
        Assert.Contains("CarKitDashboardSceneDelegate", dashboardOnly, StringComparison.Ordinal);
        Assert.DoesNotContain("InstrumentCluster", dashboardOnly, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Ios, ConfigureIos, "CarKitIosDashboard", "Navigation", false)]
    [InlineData(Ios, ConfigureIos, "CarKitIosInstrumentCluster", "Navigation", false)]
    [InlineData(Ios, ConfigureIos, "CarKitIosDashboard", "Parking", true)]
    [InlineData(Android, ConfigureAndroid, "CarKitAndroidCluster", "Navigation", false)]
    [InlineData(Android, ConfigureAndroid, "CarKitAndroidCluster", "Weather", true)]
    public void Second_screens_without_navigation_and_module_fail_with_carkit009(string platform, string target, string property, string categories, bool module)
    {
        var properties = new Dictionary<string, string> { [property] = "true" };
        if (module)
        {
            properties["CarKitNavigationModule"] = "true";
        }

        var result = Probe(platform, target, categories, properties);

        Assert.NotEqual(0, result.ExitCode);
        Assert.True(result.HasDiagnostic("error", "CARKIT009"), result.Log);
        Assert.Contains(property, result.Log, StringComparison.Ordinal);
    }

    [Fact]
    public void Android_cluster_adds_feature_cluster_category()
    {
        var result = Probe(Android, ConfigureAndroid, "Navigation", Navigation(new() { ["CarKitAndroidCluster"] = "true" }));

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(
            ["androidx.car.app.category.FEATURE_CLUSTER", "androidx.car.app.category.NAVIGATION"],
            result.Identities("CarKitAndroidIntentCategory").Order(StringComparer.Ordinal));
        Assert.Contains("androidx.car.app.category.FEATURE_CLUSTER", result.Property("CarKitAndroidIntentCategories"), StringComparison.Ordinal);
        Assert.DoesNotContain("FEATURE_CLUSTER", Probe(Android, ConfigureAndroid, "Navigation", Navigation()).Property("CarKitAndroidIntentCategories"), StringComparison.Ordinal);
    }

    // POI and weather with an app map (MapWithContentTemplate) draw on the surface: ACCESS_SURFACE, weather also
    // MAP_TEMPLATES
    [Theory]
    [InlineData("Parking", new[] { "androidx.car.app.ACCESS_SURFACE", "androidx.car.app.MAP_TEMPLATES" })]
    [InlineData("Weather", new[] { "androidx.car.app.ACCESS_SURFACE", "androidx.car.app.MAP_TEMPLATES" })]
    [InlineData("Weather;Navigation", new[] { "androidx.car.app.ACCESS_SURFACE", "androidx.car.app.NAVIGATION_TEMPLATES" })]
    [InlineData("Iot", new string[0])]
    public void Poi_and_weather_with_navigation_module_get_surface_access(string categories, string[] permissions)
    {
        var result = Probe(Android, ConfigureAndroid, categories, Navigation());

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(permissions, result.Identities("CarKitAndroidPermission").Order(StringComparer.Ordinal));
        Assert.Empty(Probe(Android, ConfigureAndroid, "Weather").Identities("CarKitAndroidPermission"));
        Assert.Equal(["androidx.car.app.MAP_TEMPLATES"], Probe(Android, ConfigureAndroid, "Parking").Identities("CarKitAndroidPermission"));
    }

    private static Dictionary<string, string> Navigation(Dictionary<string, string>? extra = null)
    {
        var properties = new Dictionary<string, string>(extra ?? []) { ["CarKitNavigationModule"] = "true" };
        return properties;
    }

    private static ProbeResult Probe(string platform, string target, string? categories, Dictionary<string, string>? extra = null)
    {
        var properties = new Dictionary<string, string>(extra ?? []);
        if (categories is not null)
        {
            properties["CarKitCategories"] = categories;
        }

        return MSBuildProbe.Run(platform, target, properties);
    }
}
