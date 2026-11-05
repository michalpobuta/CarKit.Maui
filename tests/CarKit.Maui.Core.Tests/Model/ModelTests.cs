using Xunit;

#pragma warning disable CARKIT0001 // CarPlayVideoBrowseTemplate is experimental

namespace CarKit.Maui.Core.Tests.Model;

public class SpecExampleTests
{
    private sealed record Station(string Name, string Address, int FreeSlots);

    [Fact]
    public void Spec_usage_example_builds_expected_model()
    {
        ICarSession session = null!;
        Station[] stations = [new("Fuelco", "Market Street 1", 3), new("BP", "Park Avenue 10", 0)];

        var items = stations.Select(s => new CarListItem(s.Name)
        {
            Detail = s.Address,
            Image = CarImage.FromFile("station.png"),
            IsBrowsable = true,
            OnSelected = _ => session.PushAsync(new CarInformationTemplate(s.Name)
            {
                Items = [new("Free slots", s.FreeSlots.ToString(System.Globalization.CultureInfo.InvariantCulture))],
                Actions = [new CarAction("Navigate", _ => Task.CompletedTask)],
            }),
        });
        var root = new CarListTemplate("Nearby") { Sections = [new CarSection(items)] };

        Assert.Equal(CarTemplateKind.List, root.Kind);
        Assert.Equal("Nearby", root.Title?.Text);
        var section = Assert.Single(root.Sections);
        Assert.Collection(section.Items,
            i => Assert.Equal("Fuelco", Assert.IsType<CarListItem>(i).Text.Text),
            i => Assert.Equal("Park Avenue 10", Assert.IsType<CarListItem>(i).Detail?.Text));
    }
}

public class TemplateEqualityTests
{
    [Fact]
    public void Templates_with_same_ids_and_content_are_deeply_equal()
    {
        static CarListTemplate Build() => new("Stations")
        {
            Id = "root",
            Sections = [new CarSection([new CarListItem("A") { Id = "a" }]) { Id = "s1", Header = "Close" }],
            TrailingBarButtons = [new CarBarButton("Search") { Id = "search" }],
        };

        Assert.Equal(Build(), Build());
        Assert.Equal(Build().GetHashCode(), Build().GetHashCode());
    }

    [Fact]
    public void With_expression_keeps_id_and_changes_content()
    {
        var list = new CarListTemplate("Stations");
        var updated = list with { Sections = [new CarSection()] };

        Assert.Equal(list.Id, updated.Id);
        Assert.NotEqual(list, updated);
    }
}

public class FallbackTests
{
    [Fact]
    public void WithFallback_returns_same_type_with_fallback()
    {
        var contact = new CarPlayContactTemplate("Anna");
        var fallback = new CarInformationTemplate("Anna");

        CarPlayContactTemplate result = contact.WithFallback(fallback);

        Assert.Same(fallback, result.Fallback);
        Assert.Equal(contact.Id, result.Id);
    }

    [Fact]
    public void WithFallback_validates_arguments()
    {
        var list = new CarListTemplate("A");

        Assert.Throws<ArgumentNullException>(() => list.WithFallback(null!));
        Assert.Throws<ArgumentNullException>(() => ((CarListTemplate)null!).WithFallback(list));
        Assert.Throws<ArgumentException>(() => list.WithFallback(list));
    }
}

public class TemplateCatalogTests
{
    internal static IReadOnlyList<CarTemplate> OneOfEach() =>
    [
        new CarListTemplate("List"),
        new CarGridTemplate("Grid"),
        new CarTabTemplate([new CarListTemplate("Tab")]),
        new CarInformationTemplate("Info"),
        new CarSearchTemplate(),
        new CarPointOfInterestTemplate("POI"),
        new CarAlertTemplate("Alert"),
        new CarMessageTemplate("Message"),
        new CarNowPlayingTemplate(),
        // Stage 8: Android Auto requires a bar button (a NavigationTemplate without an ActionStrip is rejected)
        new CarMapTemplate { TrailingBarButtons = [new CarBarButton("Destination")] },
        new CarActionSheetTemplate("Sheet", "Content"),
        new CarLongMessageTemplate("Terms", "Long text"),
        new CarVoiceControlTemplate([new CarVoiceControlState("listening", "Listening")]),
        new CarPlayContactTemplate("Anna"),
        new AndroidAutoSignInTemplate("Sign in", new AndroidAutoPinSignIn("1234")),
        new CarPlayVideoBrowseTemplate("Video"),
    ];

    [Fact]
    public void All_sixteen_template_kinds_are_distinct()
    {
        var kinds = OneOfEach().Select(t => t.Kind).ToList();

        Assert.Equal(16, kinds.Distinct().Count());
        Assert.Equal(Enum.GetValues<CarTemplateKind>().Order(), kinds.Order());
    }

    [Fact]
    public void Modal_templates_are_alert_action_sheet_and_voice_control()
    {
        var modals = OneOfEach().OfType<CarModalTemplate>().Select(t => t.Kind).Order();

        Assert.Equal([CarTemplateKind.Alert, CarTemplateKind.ActionSheet, CarTemplateKind.VoiceControl], modals.ToArray().Order());
    }
}

public class ElementValidationTests
{
    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void Playback_progress_outside_zero_one_is_rejected(double progress) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new CarListItem("Track") { PlaybackProgress = progress });

    [Fact]
    public void Required_values_are_validated()
    {
        Assert.Throws<ArgumentException>(() => new CarVoiceControlState(" ", "Listening"));
        Assert.Throws<ArgumentException>(() => new AndroidAutoPinSignIn(""));
        Assert.Throws<ArgumentNullException>(() => new CarTabTemplate(null!));
        Assert.Throws<ArgumentNullException>(() => new CarListItem("A") { Text = null! });
        Assert.Throws<ArgumentException>(() => new CarAction("A") { Id = "" });
        Assert.Throws<ArgumentNullException>(() => new AndroidAutoQrCodeSignIn(null!));
        Assert.Throws<ArgumentException>(() => new AndroidAutoPinSignIn("1234567890123"));
    }

    [Fact]
    public void Playback_progress_within_range_is_kept()
    {
        Assert.Equal(0.5, new CarListItem("Track") { PlaybackProgress = 0.5 }.PlaybackProgress);
    }
}

public class PlatformHooksTests
{
    [Fact]
    public void Hooks_combine_per_platform()
    {
        var calls = new List<string>();
        Action<string> first = s => calls.Add("1" + s);
        Action<string> second = s => calls.Add("2" + s);

        var hooks = CarPlatformHooks.None.Add(CarPlatform.CarPlay, first).Add(CarPlatform.CarPlay, second).Add(CarPlatform.AndroidAutomotive, first);
        ((Action<string>)hooks.CarPlay!)("x");

        Assert.Equal(["1x", "2x"], calls);
        Assert.Same(first, hooks.AndroidAuto);
        Assert.Equal(new CarPlatformHooks(), CarPlatformHooks.None);
    }

    [Fact]
    public void Template_hook_helper_attaches_hook()
    {
        Action<object> hook = _ => { };
        var list = new CarListTemplate("A").WithPlatformHook(CarPlatform.AndroidAuto, hook);

        Assert.Same(hook, list.PlatformHooks.AndroidAuto);
        Assert.Null(list.PlatformHooks.CarPlay);
    }
}

public class ModelWalkerTests
{
    [Fact]
    public void Walker_finds_elements_in_nested_templates()
    {
        var item = new CarListItem("A");
        var action = new CarAction("Go");
        var bar = new CarBarButton(CarImage.FromSystemSymbol("magnifyingglass", "search"));
        var list = new CarListTemplate("List") { Sections = [new CarSection([item])], LeadingBarButtons = [bar] };
        var info = new CarInformationTemplate("Info") { Actions = [action] };
        var tabs = new CarTabTemplate([list, info]);

        var ids = CarModelWalker.Elements(tabs).Select(e => e.Id).ToHashSet();

        Assert.Contains(item.Id, ids);
        Assert.Contains(action.Id, ids);
        Assert.Contains(bar.Id, ids);
        Assert.Contains(list.Id, ids);
        Assert.Same(action, CarModelWalker.Find(tabs, action.Id));
        Assert.Null(CarModelWalker.Find(tabs, "missing"));
    }

    [Fact]
    public void Walker_covers_every_template_kind()
    {
        var poiAction = new CarAction("Navigate");
        var point = new CarPointOfInterest(new CarLocation(52, 21), "Station") { PrimaryAction = poiAction };
        var provider = new CarAction("Google");
        CarTemplate[] templates =
        [
            new CarGridTemplate("G") { Items = [new CarGridItem("A", CarImage.FromFile("a.png")) { Id = "grid" }] },
            new CarPointOfInterestTemplate("P") { Points = [point] },
            new CarAlertTemplate("A") { Actions = [new CarAction("OK") { Id = "alert" }] },
            new CarActionSheetTemplate(null, "M") { Actions = [new CarAction("OK") { Id = "sheet" }] },
            new CarMessageTemplate("M") { Actions = [new CarAction("OK") { Id = "message" }] },
            new CarLongMessageTemplate("L", "M") { Actions = [new CarAction("OK") { Id = "long" }] },
            new CarNowPlayingTemplate { Buttons = [new CarNowPlayingButton(CarNowPlayingButtonKind.Shuffle) { Id = "shuffle" }] },
            new CarMapTemplate { MapButtons = [new CarMapButton(CarImage.FromFile("z.png")) { Id = "zoom" }] },
            new CarPlayContactTemplate("Anna") { Buttons = [new CarPlayContactButton(CarPlayContactButtonKind.Call) { Id = "call" }] },
            new AndroidAutoSignInTemplate("S", new AndroidAutoProviderSignIn(provider)) { Actions = [new CarAction("Help") { Id = "help" }] },
            new CarPlayVideoBrowseTemplate("V") { Sections = [new CarSection([new CarListItem("Film") { Id = "film" }])] },
            new CarSearchTemplate(),
            new CarVoiceControlTemplate([new CarVoiceControlState("l", "Listening")]),
        ];

        var ids = templates.SelectMany(CarModelWalker.Elements).Select(e => e.Id).ToHashSet();

        Assert.Superset(new HashSet<string> { "grid", point.Id, poiAction.Id, "alert", "sheet", "message", "long", "shuffle", "zoom", "call", "help", provider.Id, "film" }, ids);
    }
}
