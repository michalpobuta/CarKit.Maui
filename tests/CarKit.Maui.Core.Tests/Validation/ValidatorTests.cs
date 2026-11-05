using CarKit.Maui.Core.Tests.Model;
using Microsoft.Extensions.Logging;
using Xunit;

namespace CarKit.Maui.Core.Tests.Validation;

internal static class V
{
    public static CarTemplateValidator Create(
        CarAppCategory categories,
        CarPlatform platform = CarPlatform.CarPlay,
        Version? ios = null,
        int? apiLevel = null,
        Action<CarKitOptions>? configure = null,
        Func<CarLimitKind, int?>? runtimeLimits = null,
        TestLogger? logger = null,
        Func<CarFeature, bool>? assetFeatures = null)
    {
        var options = new CarKitOptions { Categories = categories, Validation = CarValidationMode.Strict };
        configure?.Invoke(options);
        var capabilities = platform == CarPlatform.CarPlay
            ? new CarCapabilities(platform, ios ?? new Version(27, 0), null, runtimeLimits, assetFeatures)
            : new CarCapabilities(platform, null, apiLevel ?? 8, runtimeLimits, assetFeatures);
        return new CarTemplateValidator(options, capabilities, logger ?? new TestLogger());
    }

    public static CarTemplate Of(CarTemplateKind kind) => TemplateCatalogTests.OneOfEach().Single(t => t.Kind == kind);

    public static CarListTemplate List(int sections, int itemsPerSection) => new("List")
    {
        Sections = [.. Enumerable.Range(0, sections).Select(s => new CarSection(Enumerable.Range(0, itemsPerSection).Select(i => new CarListItem($"{s}-{i}"))))],
    };
}

public class RulesTests
{
    [Theory]
    [InlineData(CarPlatform.CarPlay)]
    [InlineData(CarPlatform.AndroidAuto)]
    [InlineData(CarPlatform.AndroidAutomotive)]
    public void Embedded_rules_parse(CarPlatform platform)
    {
        var rules = CarPlatformRules.For(platform);

        Assert.NotEmpty(rules.Templates);
        Assert.NotEmpty(rules.Categories);
        Assert.NotEmpty(rules.TabChildren);
        Assert.All(rules.Categories.Values, c => Assert.InRange(c.MaxDepth, 1, 10));
        Assert.Same(rules, CarPlatformRules.For(platform));
    }

    [Theory]
    [InlineData("""{ "templates": { "Bogus": {} } }""")]
    [InlineData("""{ "categories": { "Bogus": { "templates": {}, "maxDepth": 5 } } }""")]
    [InlineData("""{ "tabChildren": [ "Bogus" ] }""")]
    [InlineData("""{ "defaultLimits": { "Bogus": 3 } }""")]
    [InlineData("""{ "templates": { "List": { "minVersion": "abc" } } }""")]
    [InlineData("null")]
    public void Unknown_names_in_rules_are_rejected(string json) =>
        Assert.Throws<InvalidDataException>(() => CarPlatformRules.Parse(json));

    [Fact]
    public void Unverified_categories_are_marked()
    {
        var rules = CarPlatformRules.For(CarPlatform.CarPlay);

        Assert.False(rules.Categories[CarAppCategory.Video].Verified);
        Assert.True(rules.Categories[CarAppCategory.Audio].Verified);
    }
}

public class CategoryMatrixTests
{
    [Theory]
    [InlineData(CarAppCategory.Audio, CarTemplateKind.NowPlaying)]
    [InlineData(CarAppCategory.Audio, CarTemplateKind.Tab)]
    [InlineData(CarAppCategory.Communication, CarTemplateKind.CarPlayContact)]
    [InlineData(CarAppCategory.Communication, CarTemplateKind.Information)]
    [InlineData(CarAppCategory.Navigation, CarTemplateKind.Map)]
    [InlineData(CarAppCategory.Navigation, CarTemplateKind.Search)]
    [InlineData(CarAppCategory.Parking, CarTemplateKind.PointOfInterest)]
    [InlineData(CarAppCategory.EvCharging, CarTemplateKind.PointOfInterest)]
    [InlineData(CarAppCategory.Fueling, CarTemplateKind.Grid)]
    [InlineData(CarAppCategory.QuickFoodOrdering, CarTemplateKind.PointOfInterest)]
    [InlineData(CarAppCategory.DrivingTask, CarTemplateKind.Information)]
    [InlineData(CarAppCategory.Audio, CarTemplateKind.Alert)]
    [InlineData(CarAppCategory.Audio, CarTemplateKind.VoiceControl)]
    public void CarPlay_allows_templates_from_the_matrix(CarAppCategory category, CarTemplateKind kind)
    {
        var template = V.Of(kind);

        Assert.Same(template, V.Create(category).Prepare(template, resultingDepth: 1));
    }

    [Theory]
    [InlineData(CarAppCategory.Audio, CarTemplateKind.Information)]
    [InlineData(CarAppCategory.Audio, CarTemplateKind.PointOfInterest)]
    [InlineData(CarAppCategory.Communication, CarTemplateKind.Search)]
    [InlineData(CarAppCategory.Navigation, CarTemplateKind.PointOfInterest)]
    [InlineData(CarAppCategory.Parking, CarTemplateKind.Map)]
    [InlineData(CarAppCategory.DrivingTask, CarTemplateKind.NowPlaying)]
    [InlineData(CarAppCategory.Fueling, CarTemplateKind.Search)]
    [InlineData(CarAppCategory.Iot, CarTemplateKind.List)]
    [InlineData(CarAppCategory.None, CarTemplateKind.List)]
    public void CarPlay_rejects_templates_outside_the_matrix(CarAppCategory category, CarTemplateKind kind)
    {
        var ex = Assert.Throws<CarTemplateNotAllowedException>(() => V.Create(category).Prepare(V.Of(kind), resultingDepth: 1));

        Assert.Equal(kind, ex.Kind);
        Assert.Equal(category, ex.Categories);
    }

    [Fact]
    public void Voice_control_needs_ios_27_outside_navigation()
    {
        var voice = V.Of(CarTemplateKind.VoiceControl);

        Assert.Throws<CarTemplateNotAllowedException>(() => V.Create(CarAppCategory.Audio, ios: new Version(26, 4)).Prepare(voice, null));
        Assert.Same(voice, V.Create(CarAppCategory.Navigation, ios: new Version(26, 4)).Prepare(voice, null));
    }

    [Fact]
    public void Any_declared_category_can_allow_a_template()
    {
        var validator = V.Create(CarAppCategory.Audio | CarAppCategory.Parking);

        Assert.NotNull(validator.Prepare(V.Of(CarTemplateKind.PointOfInterest), 1));
        Assert.NotNull(validator.Prepare(V.Of(CarTemplateKind.NowPlaying), 1));
    }

    [Theory]
    [InlineData(CarAppCategory.Iot, CarTemplateKind.Grid)]
    [InlineData(CarAppCategory.Weather, CarTemplateKind.PointOfInterest)]
    [InlineData(CarAppCategory.Navigation, CarTemplateKind.Map)]
    [InlineData(CarAppCategory.Parking, CarTemplateKind.AndroidAutoSignIn)]
    public void Android_Auto_allows_category_templates(CarAppCategory category, CarTemplateKind kind) =>
        Assert.NotNull(V.Create(category, CarPlatform.AndroidAuto).Prepare(V.Of(kind), 1));

    [Theory]
    [InlineData(CarAppCategory.Iot, CarTemplateKind.Map)]
    [InlineData(CarAppCategory.QuickFoodOrdering, CarTemplateKind.List)]
    public void Android_Auto_rejects_templates_outside_its_rules(CarAppCategory category, CarTemplateKind kind) =>
        Assert.Throws<CarTemplateNotAllowedException>(() => V.Create(category, CarPlatform.AndroidAuto).Prepare(V.Of(kind), 1));
}

public class DepthTests
{
    [Theory]
    [InlineData(CarAppCategory.Audio, CarPlatform.CarPlay, 5)]
    [InlineData(CarAppCategory.Fueling, CarPlatform.CarPlay, 3)]
    [InlineData(CarAppCategory.QuickFoodOrdering, CarPlatform.CarPlay, 3)]
    [InlineData(CarAppCategory.Parking, CarPlatform.AndroidAuto, 5)]
    public void Depth_limit_comes_from_the_category(CarAppCategory category, CarPlatform platform, int maxDepth)
    {
        var validator = V.Create(category, platform);
        var list = new CarListTemplate("List");

        Assert.Same(list, validator.Prepare(list, maxDepth));
        var ex = Assert.Throws<CarTemplateLimitException>(() => validator.Prepare(list, maxDepth + 1));
        Assert.Equal(CarLimitKind.StackDepth, ex.Limit);
        Assert.Equal(maxDepth, ex.Maximum);
        Assert.Equal(maxDepth + 1, ex.Actual);
    }

    [Fact]
    public void Most_permissive_category_wins() =>
        Assert.NotNull(V.Create(CarAppCategory.Fueling | CarAppCategory.Audio).Prepare(new CarListTemplate("A"), 5));
}

public class SupportAndFallbackTests
{
    [Fact]
    public void Old_ios_without_fallback_is_not_supported()
    {
        var ex = Assert.Throws<CarTemplateNotSupportedException>(() =>
            V.Create(CarAppCategory.Audio, ios: new Version(13, 0)).Prepare(V.Of(CarTemplateKind.Tab), 1));

        Assert.Equal(CarTemplateKind.Tab, ex.Kind);
        Assert.Equal(CarPlatform.CarPlay, ex.Platform);
    }

    [Fact]
    public void Explicit_fallback_is_used_and_keeps_the_id()
    {
        var tabs = new CarTabTemplate([new CarListTemplate("A")]).WithFallback(new CarListTemplate("Substitute"));

        var resolved = V.Create(CarAppCategory.Audio, ios: new Version(13, 0)).Prepare(tabs, 1);

        Assert.IsType<CarListTemplate>(resolved);
        Assert.Equal(tabs.Id, resolved.Id);
    }

    [Fact]
    public void Platform_only_template_needs_explicit_fallback()
    {
        var contact = new CarPlayContactTemplate("Anna");
        var validator = V.Create(CarAppCategory.Communication, CarPlatform.AndroidAuto);

        Assert.Throws<CarTemplateNotSupportedException>(() => validator.Prepare(contact, 1));
        Assert.IsType<CarInformationTemplate>(validator.Prepare(contact.WithFallback(new CarInformationTemplate("Anna")), 1));
    }

    [Fact]
    public void Action_sheet_falls_back_to_message_on_android()
    {
        var action = new CarAction("Delete");
        var sheet = new CarActionSheetTemplate("Title", "Are you sure?") { Actions = [action] };

        var message = Assert.IsType<CarMessageTemplate>(V.Create(CarAppCategory.Iot, CarPlatform.AndroidAuto).Prepare(sheet, null));

        Assert.Equal("Are you sure?", message.Message.Text);
        Assert.Equal("Title", message.Title?.Text);
        Assert.Equal([action], message.Actions);
        Assert.Equal(sheet.Id, message.Id);
    }

    [Fact]
    public void Long_message_falls_back_to_information_on_carplay()
    {
        var info = Assert.IsType<CarInformationTemplate>(
            V.Create(CarAppCategory.Communication).Prepare(new CarLongMessageTemplate("Terms", "Content"), 1));

        Assert.Equal("Content", Assert.Single(info.Items).Detail.Text);
    }

    [Fact]
    public void Voice_control_falls_back_to_message_with_active_state()
    {
        var voice = new CarVoiceControlTemplate([new CarVoiceControlState("listening", "Listening"), new CarVoiceControlState("thinking", "Thinking")])
        {
            ActiveStateId = "thinking",
        };

        var message = Assert.IsType<CarMessageTemplate>(V.Create(CarAppCategory.Navigation, CarPlatform.AndroidAuto).Prepare(voice, null));

        Assert.Equal("Thinking", message.Message.Text);
    }

    [Fact]
    public void Alert_falls_back_to_message_below_api_5()
    {
        var alert = new CarAlertTemplate("Attention") { Subtitle = "Gate open" };

        var message = Assert.IsType<CarMessageTemplate>(V.Create(CarAppCategory.Iot, CarPlatform.AndroidAuto, apiLevel: 4).Prepare(alert, null));

        Assert.Equal("Gate open", message.Message.Text);
        Assert.Same(alert, V.Create(CarAppCategory.Iot, CarPlatform.AndroidAuto, apiLevel: 5).Prepare(alert, null));
    }

    [Fact]
    public void Throw_behavior_ignores_fallbacks()
    {
        var validator = V.Create(CarAppCategory.Iot, CarPlatform.AndroidAuto, configure: o => o.UnsupportedTemplateBehavior = UnsupportedTemplateBehavior.Throw);

        Assert.Throws<CarTemplateNotSupportedException>(() => validator.Prepare(new CarActionSheetTemplate("A", "B"), null));
    }

    [Fact]
    public void Fallback_chain_that_never_resolves_throws()
    {
        var contact = new CarPlayContactTemplate("A").WithFallback(new CarPlayContactTemplate("B"));

        Assert.Throws<CarTemplateNotSupportedException>(() => V.Create(CarAppCategory.Communication, CarPlatform.AndroidAuto).Prepare(contact, 1));
    }
}

public class TabTests
{
    [Fact]
    public void Tabs_accept_allowed_children()
    {
        var tabs = new CarTabTemplate([new CarListTemplate("A"), new CarGridTemplate("B")]);

        Assert.Same(tabs, V.Create(CarAppCategory.Audio).Prepare(tabs, 1));
    }

    [Theory]
    [InlineData(CarTemplateKind.Map)]
    [InlineData(CarTemplateKind.Tab)]
    public void Tabs_reject_children_the_platform_does_not_allow(CarTemplateKind childKind)
    {
        var tabs = new CarTabTemplate([V.Of(childKind)]);

        Assert.Throws<CarTemplateNotAllowedException>(() => V.Create(CarAppCategory.Navigation).Prepare(tabs, 1));
    }

    [Fact]
    public void Tab_children_are_checked_against_categories() =>
        Assert.Throws<CarTemplateNotAllowedException>(() =>
            V.Create(CarAppCategory.Audio).Prepare(new CarTabTemplate([new CarInformationTemplate("A")]), 1));

    [Fact]
    public void Tab_children_get_fallbacks_and_limits()
    {
        var child = new CarLongMessageTemplate("A", "B");
        var tabs = new CarTabTemplate([child, V.List(1, 20)]);

        var resolved = Assert.IsType<CarTabTemplate>(V.Create(CarAppCategory.Communication).Prepare(tabs, 1));

        Assert.IsType<CarInformationTemplate>(resolved.Tabs[0]);
        Assert.Equal(12, Assert.IsType<CarListTemplate>(resolved.Tabs[1]).Sections[0].Items.Count);
        Assert.Equal(tabs.Id, resolved.Id);
    }
}

public class LimitTests
{
    [Fact]
    public void List_is_truncated_across_sections()
    {
        var resolved = Assert.IsType<CarListTemplate>(V.Create(CarAppCategory.Audio).Prepare(V.List(3, 5), 1));

        Assert.Equal([5, 5, 2], resolved.Sections.Select(s => s.Items.Count));
    }

    [Fact]
    public void Sections_beyond_limit_are_dropped()
    {
        var resolved = Assert.IsType<CarListTemplate>(V.Create(CarAppCategory.Audio).Prepare(V.List(8, 1), 1));

        Assert.Equal(5, resolved.Sections.Count);
    }

    [Fact]
    public void Runtime_limits_override_defaults()
    {
        var validator = V.Create(CarAppCategory.Audio, runtimeLimits: kind => kind == CarLimitKind.ListItems ? 3 : null);

        var resolved = Assert.IsType<CarListTemplate>(validator.Prepare(V.List(1, 10), 1));

        Assert.Equal(3, resolved.Sections[0].Items.Count);
    }

    [Fact]
    public void Grid_points_information_and_actions_are_truncated()
    {
        var image = CarImage.FromFile("a.png");
        var grid = new CarGridTemplate("G") { Items = [.. Enumerable.Range(0, 10).Select(i => new CarGridItem($"{i}", image))] };
        var poi = new CarPointOfInterestTemplate("P") { Points = [.. Enumerable.Range(0, 15).Select(i => new CarPointOfInterest(new CarLocation(0, 0), $"{i}"))] };
        var info = new CarInformationTemplate("I")
        {
            Items = [.. Enumerable.Range(0, 12).Select(i => new CarInformationItem(null, $"{i}"))],
            Actions = [new CarAction("1"), new CarAction("2"), new CarAction("3"), new CarAction("4")],
        };
        var validator = V.Create(CarAppCategory.Parking);

        Assert.Equal(8, Assert.IsType<CarGridTemplate>(validator.Prepare(grid, 1)).Items.Count);
        Assert.Equal(12, Assert.IsType<CarPointOfInterestTemplate>(validator.Prepare(poi, 1)).Points.Count);
        var resolvedInfo = Assert.IsType<CarInformationTemplate>(validator.Prepare(info, 1));
        Assert.Equal(10, resolvedInfo.Items.Count);
        Assert.Equal(3, resolvedInfo.Actions.Count);
    }

    [Fact]
    public void Message_alert_and_tab_limits_apply_on_android()
    {
        CarArray<CarAction> actions = [new CarAction("1"), new CarAction("2"), new CarAction("3")];
        var validator = V.Create(CarAppCategory.Iot, CarPlatform.AndroidAuto);

        Assert.Equal(2, Assert.IsType<CarMessageTemplate>(validator.Prepare(new CarMessageTemplate("M") { Actions = actions }, 1)).Actions.Count);
        Assert.Equal(2, Assert.IsType<CarAlertTemplate>(validator.Prepare(new CarAlertTemplate("A") { Actions = actions }, null)).Actions.Count);
        Assert.Equal(2, Assert.IsType<CarLongMessageTemplate>(validator.Prepare(new CarLongMessageTemplate("L", "M") { Actions = actions }, 1)).Actions.Count);
        Assert.Equal(2, Assert.IsType<AndroidAutoSignInTemplate>(validator.Prepare(new AndroidAutoSignInTemplate("S", new AndroidAutoPinSignIn("1")) { Actions = actions }, 1)).Actions.Count);
        var tabs = new CarTabTemplate(Enumerable.Range(0, 6).Select(i => new CarListTemplate($"{i}")));
        Assert.Equal(4, Assert.IsType<CarTabTemplate>(validator.Prepare(tabs, 1)).Tabs.Count);
    }

    [Fact]
    public void Throw_in_strict_mode_raises_limit_exception()
    {
        var validator = V.Create(CarAppCategory.Audio, configure: o => o.Limits.OnOverflow = CarOverflowBehavior.Throw);

        var ex = Assert.Throws<CarTemplateLimitException>(() => validator.Prepare(V.List(1, 20), 1));

        Assert.Equal(CarLimitKind.ListItems, ex.Limit);
        Assert.Equal(12, ex.Maximum);
        Assert.Equal(20, ex.Actual);
    }

    [Fact]
    public void Warn_mode_logs_and_keeps_the_template()
    {
        var logger = new TestLogger();
        var validator = V.Create(CarAppCategory.Audio, logger: logger, configure: o =>
        {
            o.Limits.OnOverflow = CarOverflowBehavior.Throw;
            o.Validation = CarValidationMode.Warn;
        });
        var list = V.List(1, 20);

        Assert.Same(list, validator.Prepare(list, 9));
        Assert.Equal(2, logger.Entries.Count(e => e.Level == LogLevel.Warning));
    }

    [Fact]
    public void Off_mode_skips_category_and_limit_checks()
    {
        var validator = V.Create(CarAppCategory.Audio, configure: o => o.Validation = CarValidationMode.Off);
        var info = new CarInformationTemplate("I");

        Assert.Same(info, validator.Prepare(info, 99));
    }

    [Fact]
    public void Off_mode_still_resolves_fallbacks()
    {
        var validator = V.Create(CarAppCategory.Iot, CarPlatform.AndroidAuto, configure: o => o.Validation = CarValidationMode.Off);

        Assert.IsType<CarMessageTemplate>(validator.Prepare(new CarActionSheetTemplate("A", null), null));
    }
}

public class OptionsTests
{
    [Theory]
    [InlineData("Warn", CarValidationMode.Warn)]
    [InlineData("off", CarValidationMode.Off)]
    [InlineData("Strict", CarValidationMode.Strict)]
    [InlineData("bogus", CarValidationMode.Strict)]
    [InlineData(null, CarValidationMode.Strict)]
    public void Default_validation_mode_comes_from_app_context(string? configured, CarValidationMode expected) =>
        Assert.Equal(expected, CarKitOptions.ResolveValidationMode(configured));

    [Fact]
    public void Defaults_match_the_spec()
    {
        var options = new CarKitOptions();

        Assert.Equal(UnsupportedTemplateBehavior.UseFallbackOrThrow, options.UnsupportedTemplateBehavior);
        Assert.Equal(CarOverflowBehavior.Truncate, options.Limits.OnOverflow);
        Assert.False(options.Diagnostics.LogNativeCalls);
        Assert.Equal(CarAppCategory.None, options.Categories);
    }
}

public class CapabilitiesTests
{
    [Fact]
    public void Supports_checks_kind_and_version()
    {
        ICarCapabilities ios13 = new CarCapabilities(CarPlatform.CarPlay, new Version(13, 0), null);
        ICarCapabilities api5 = new CarCapabilities(CarPlatform.AndroidAuto, null, 5);

        Assert.True(ios13.Supports<CarListTemplate>());
        Assert.False(ios13.Supports<CarTabTemplate>());
        Assert.False(ios13.Supports(CarTemplateKind.AndroidAutoSignIn));
        Assert.False(api5.Supports<CarTabTemplate>());
        Assert.True(api5.Supports<CarAlertTemplate>());
        Assert.Equal(new Version(13, 0), ios13.IosVersion);
        Assert.Equal(5, api5.CarAppApiLevel);
        Assert.Null(api5.GetLimit(CarLimitKind.ListSections));
        Assert.Equal(6, api5.GetLimit(CarLimitKind.ListItems));
    }

    [Fact]
    public void Supports_rejects_abstract_template_types() =>
        Assert.Throws<ArgumentException>(() => ((ICarCapabilities)new CarCapabilities(CarPlatform.CarPlay, new Version(27, 0), null)).Supports<CarTemplate>());

    [Fact]
    public void Every_concrete_template_type_maps_to_its_kind()
    {
        ICarCapabilities caps = new CarCapabilities(CarPlatform.CarPlay, new Version(27, 0), null);
        bool[] results =
        [
            caps.Supports<CarListTemplate>(), caps.Supports<CarGridTemplate>(), caps.Supports<CarTabTemplate>(),
            caps.Supports<CarInformationTemplate>(), caps.Supports<CarSearchTemplate>(), caps.Supports<CarPointOfInterestTemplate>(),
            caps.Supports<CarAlertTemplate>(), caps.Supports<CarMessageTemplate>(), caps.Supports<CarNowPlayingTemplate>(),
            caps.Supports<CarMapTemplate>(), caps.Supports<CarActionSheetTemplate>(), caps.Supports<CarLongMessageTemplate>(),
            caps.Supports<CarVoiceControlTemplate>(), caps.Supports<CarPlayContactTemplate>(), caps.Supports<AndroidAutoSignInTemplate>(),
#pragma warning disable CARKIT0001
            caps.Supports<CarPlayVideoBrowseTemplate>(),
#pragma warning restore CARKIT0001
        ];

        Assert.Equal(14, results.Count(r => r));
    }
}

public class ExceptionTests
{
    public static TheoryData<Func<string?, Exception?, Exception>> Factories => new()
    {
        (m, i) => i is null ? new CarKitException(m) : new CarKitException(m, i),
        (m, i) => i is null ? new CarNotConnectedException(m) : new CarNotConnectedException(m, i),
        (m, i) => i is null ? new CarTemplateNotAllowedException(m) : new CarTemplateNotAllowedException(m, i),
        (m, i) => i is null ? new CarTemplateNotSupportedException(m) : new CarTemplateNotSupportedException(m, i),
        (m, i) => i is null ? new CarTemplateLimitException(m) : new CarTemplateLimitException(m, i),
        (m, i) => i is null ? new CarStackException(m) : new CarStackException(m, i),
        (m, i) => i is null ? new CarPlatformException(m) : new CarPlatformException(m, i),
    };

    [Theory, MemberData(nameof(Factories))]
    public void Exceptions_have_standard_constructors(Func<string?, Exception?, Exception> factory)
    {
        var inner = new InvalidOperationException();

        Assert.IsAssignableFrom<CarKitException>(factory("a", null));
        Assert.Equal("a", factory("a", null).Message);
        Assert.Same(inner, factory("b", inner).InnerException);
    }

    [Fact]
    public void Parameterless_constructors_exist()
    {
        Exception[] all =
        [
            new CarKitException(), new CarNotConnectedException(), new CarTemplateNotAllowedException(),
            new CarTemplateNotSupportedException(), new CarTemplateLimitException(), new CarStackException(), new CarPlatformException(),
        ];

        Assert.All(all, e => Assert.False(string.IsNullOrEmpty(e.Message)));
    }
}

public class TemplateStageRulesTests
{
    // CPTabBarTemplate can only be a root: pushing a CarPlay tab bar ends with an exception
    [Fact]
    public void Tab_must_be_root_on_carplay()
    {
        var tabs = new CarTabTemplate([new CarListTemplate("A"), new CarListTemplate("B")]);

        Assert.Throws<CarStackException>(() => V.Create(CarAppCategory.Parking).Prepare(tabs, resultingDepth: 2));
        Assert.Same(tabs, V.Create(CarAppCategory.Parking).Prepare(tabs, resultingDepth: 1));
        Assert.Same(tabs, V.Create(CarAppCategory.Parking).Prepare(tabs, resultingDepth: null));
        Assert.Same(tabs, V.Create(CarAppCategory.Parking, CarPlatform.AndroidAuto).Prepare(tabs, resultingDepth: 2));
    }

    [Fact]
    public void Tab_outside_root_only_warns_in_warn_mode()
    {
        var logger = new TestLogger();
        var tabs = new CarTabTemplate([new CarListTemplate("A")]);

        var resolved = V.Create(CarAppCategory.Parking, logger: logger, configure: o => o.Validation = CarValidationMode.Warn).Prepare(tabs, 2);

        Assert.Same(tabs, resolved);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("root", StringComparison.Ordinal));
    }

    [Fact]
    public void Fallbacks_keep_tab_info_and_bar_buttons()
    {
        var leading = new CarBarButton("L");
        var trailing = new CarBarButton("T");
        var tab = new CarTabInfo("Tab") { Badge = 2 };

        var info = Assert.IsType<CarInformationTemplate>(V.Create(CarAppCategory.Iot, CarPlatform.AndroidAuto, apiLevel: 1).Prepare(
            new CarLongMessageTemplate("Terms", "Content") { TabInfo = tab, LeadingBarButtons = [leading], TrailingBarButtons = [trailing] }, 1));
        var alertMessage = Assert.IsType<CarMessageTemplate>(V.Create(CarAppCategory.Iot, CarPlatform.AndroidAuto, apiLevel: 4).Prepare(
            new CarAlertTemplate("Attention") { TabInfo = tab, TrailingBarButtons = [trailing] }, null));
        var sheetMessage = Assert.IsType<CarMessageTemplate>(V.Create(CarAppCategory.Iot, CarPlatform.AndroidAuto).Prepare(
            new CarActionSheetTemplate("A", "B") { LeadingBarButtons = [leading], TrailingBarButtons = [trailing] }, null));

        Assert.Equal(tab, info.TabInfo);
        Assert.Equal([leading], info.LeadingBarButtons);
        Assert.Equal([trailing], info.TrailingBarButtons);
        Assert.Equal(tab, alertMessage.TabInfo);
        Assert.Equal([trailing], alertMessage.TrailingBarButtons);
        Assert.Equal([leading], sheetMessage.LeadingBarButtons);
        Assert.Equal([trailing], sheetMessage.TrailingBarButtons);
    }
}

// Stage 4: data needed by the platform template renderers (CPVoiceControlTemplate, CPContactMessageButton, CPButton)
public class PlatformTemplateValidationTests
{
    private static CarVoiceControlState State(string id) => new(id, id);

    [Fact]
    public void Voice_control_needs_states_with_unique_identifiers()
    {
        var validator = V.Create(CarAppCategory.Navigation);

        Assert.Throws<CarKitException>(() => validator.Prepare(new CarVoiceControlTemplate([]), null));
        Assert.Throws<CarKitException>(() => validator.Prepare(new CarVoiceControlTemplate([State("a"), State("a")]), null));
        Assert.Throws<CarKitException>(() => validator.Prepare(new CarVoiceControlTemplate([State("a")]) { ActiveStateId = "b" }, null));
        var valid = new CarVoiceControlTemplate([State("listening"), State("thinking")]) { ActiveStateId = "thinking" };
        Assert.Same(valid, validator.Prepare(valid, null));

        var logger = new TestLogger();
        var empty = new CarVoiceControlTemplate([]);
        Assert.Same(empty, V.Create(CarAppCategory.Navigation, logger: logger, configure: o => o.Validation = CarValidationMode.Warn).Prepare(empty, null));
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("VoiceControl", StringComparison.Ordinal));
    }

    [Fact]
    public void Contact_buttons_need_their_data()
    {
        var validator = V.Create(CarAppCategory.Communication);
        CarPlayContactTemplate Contact(CarPlayContactButton button) => new("Anna") { Buttons = [button] };

        Assert.Throws<CarKitException>(() => validator.Prepare(Contact(new CarPlayContactButton(CarPlayContactButtonKind.Message)), 2));
        Assert.Throws<CarKitException>(() => validator.Prepare(Contact(new CarPlayContactButton(CarPlayContactButtonKind.Custom)), 2));
        foreach (var button in new[]
        {
            new CarPlayContactButton(CarPlayContactButtonKind.Call),
            new CarPlayContactButton(CarPlayContactButtonKind.Directions),
            new CarPlayContactButton(CarPlayContactButtonKind.Message) { PhoneOrEmail = "+1 555 010 0200" },
            new CarPlayContactButton(CarPlayContactButtonKind.Custom) { Title = "Favorite" },
            new CarPlayContactButton(CarPlayContactButtonKind.Custom) { Image = CarImage.FromSystemSymbol("star", null) },
        })
        {
            var contact = Contact(button);
            Assert.Same(contact, validator.Prepare(contact, 2));
        }
    }

    [Fact]
    public void Voice_control_fallback_keeps_the_template_title()
    {
        var voice = new CarVoiceControlTemplate([State("listening") with { Title = "Listening" }]) { Title = "Assistant" };

        var message = Assert.IsType<CarMessageTemplate>(V.Create(CarAppCategory.Parking, CarPlatform.AndroidAuto).Prepare(voice, null));

        Assert.Equal("Assistant", message.Title?.Text);
        Assert.Equal("Listening", message.Message.Text);
    }
}
