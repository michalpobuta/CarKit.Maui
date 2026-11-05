using CarKit.Maui.Testing;
using CarKit.Sample.Car;
using Xunit;

namespace CarKit.Maui.Tests.Sample;

// Stage 7: the flow of each sample category separately, in Strict on FakeCarHost (spec: "Category decisions
// (Stage 7)"); the validator checks the category matrix and its depth (Fueling, quick ordering, driving task,
// conversational: 3)
public class CategorySampleTests
{
    private static async Task<(FakeCarHost Host, ICarSession Session)> ConnectAsync(CarAppCategory categories, CarPlatform platform)
    {
        var host = new FakeCarHost(categories, platform);
        host.Options.Validation = CarValidationMode.Strict;
        var session = await host.ConnectAsync(new SampleCarAppHandler(host.Options));
        return (host, session);
    }

    private static IEnumerable<CarListItem> MenuEntries(ICarSession session) =>
        ((CarListTemplate)session.Stack[0]).Sections.SelectMany(s => s.Items).OfType<CarListItem>();

    private static Task OpenAsync(FakeCarHost host, ICarSession session, string entry) =>
        host.SelectAsync(MenuEntries(session).Single(i => i.Text.Text == entry));

    public static TheoryData<CarAppCategory, CarPlatform, string[]> Menus => new()
    {
        { CarAppCategory.Parking, CarPlatform.CarPlay, ["Parking map", "Lists", "Templates", "Modals", "Platform"] },
        { CarAppCategory.Parking, CarPlatform.AndroidAuto, ["Parking map", "Lists", "Templates", "Modals", "Platform"] },
        { CarAppCategory.EvCharging | CarAppCategory.Fueling, CarPlatform.AndroidAuto, ["EV chargers", "Fuel stations"] },
        { CarAppCategory.QuickFoodOrdering | CarAppCategory.DrivingTask | CarAppCategory.VoiceConversational, CarPlatform.CarPlay, ["Quick order", "Driver tasks", "Voice assistant"] },
        { CarAppCategory.Iot | CarAppCategory.Weather, CarPlatform.AndroidAuto, ["Home", "Weather"] },
        { CarAppCategory.Iot | CarAppCategory.Weather | CarAppCategory.Fueling, CarPlatform.CarPlay, ["Fuel stations"] },
    };

    [Theory]
    [MemberData(nameof(Menus))]
    public async Task Menu_shows_flows_of_the_declared_categories(CarAppCategory categories, CarPlatform platform, string[] expected)
    {
        var (host, session) = await ConnectAsync(categories, platform);
        using var _ = host;

        Assert.Equal(expected, MenuEntries(session).Select(i => i.Text.Text));
        Assert.Empty(host.Errors);
    }

    [Theory]
    [InlineData(CarAppCategory.Parking, CarPlatform.CarPlay, "Parking map")]
    [InlineData(CarAppCategory.Parking, CarPlatform.AndroidAuto, "Parking map")]
    [InlineData(CarAppCategory.EvCharging, CarPlatform.CarPlay, "EV chargers")]
    [InlineData(CarAppCategory.EvCharging, CarPlatform.AndroidAuto, "EV chargers")]
    [InlineData(CarAppCategory.Fueling, CarPlatform.CarPlay, "Fuel stations")]
    [InlineData(CarAppCategory.Fueling, CarPlatform.AndroidAuto, "Fuel stations")]
    public async Task Poi_flow_reaches_details_and_confirmation(CarAppCategory category, CarPlatform platform, string entry)
    {
        var (host, session) = await ConnectAsync(category, platform);
        using var _ = host;

        await OpenAsync(host, session, entry);
        var map = Assert.IsType<CarPointOfInterestTemplate>(session.Stack[^1]);
        Assert.InRange(map.Points.Count, 3, 6);
        Assert.All(map.Points, point => Assert.NotNull(point.Distance));
        await host.SelectAsync(platform == CarPlatform.CarPlay ? map.Points[0].PrimaryAction! : map.Points[0]);
        var details = Assert.IsType<CarInformationTemplate>(session.Stack[^1]);
        Assert.Equal(3, session.Stack.Count);
        await host.SelectAsync(details.Actions[0]);
        var confirm = Assert.IsType<CarAlertTemplate>(session.Presented);
        await host.SelectAsync(confirm.Actions[0]);

        Assert.IsType<CarAlertTemplate>(session.Presented);
        Assert.NotEqual(confirm.Id, session.Presented!.Id);
        Assert.Empty(host.Errors);
    }

    [Fact]
    public async Task Poi_map_reacts_to_region_changes_and_refresh()
    {
        var (carPlay, carPlaySession) = await ConnectAsync(CarAppCategory.Parking, CarPlatform.CarPlay);
        using var _ = carPlay;
        await OpenAsync(carPlay, carPlaySession, "Parking map");
        await carPlay.ChangeMapRegionAsync(new CarMapRegion(new CarLocation(52.25, 21.0), 0.2, 0.2));

        var (androidAuto, androidSession) = await ConnectAsync(CarAppCategory.Parking, CarPlatform.AndroidAuto);
        using var __ = androidAuto;
        await OpenAsync(androidAuto, androidSession, "Parking map");
        var before = Assert.IsType<CarPointOfInterestTemplate>(androidSession.Stack[^1]);
        await androidAuto.RefreshAsync();
        var after = Assert.IsType<CarPointOfInterestTemplate>(androidSession.Stack[^1]);

        // Refresh: the same point titles (no task step), a different summary (free spots)
        Assert.Equal(before.Id, after.Id);
        Assert.Equal(before.Points.Select(p => p.Title.Text), after.Points.Select(p => p.Title.Text));
        Assert.NotEqual(before.Points[0].Summary?.Text, after.Points[0].Summary?.Text);
        Assert.Contains(androidAuto.Calls, c => c.Operation == FakeCarHostOperation.Update);
        Assert.Empty(carPlay.Errors);
        Assert.Empty(androidAuto.Errors);
    }

    [Fact]
    public async Task Quick_order_flow_confirms_with_an_alert()
    {
        var (host, session) = await ConnectAsync(CarAppCategory.QuickFoodOrdering, CarPlatform.CarPlay);
        using var _ = host;

        await OpenAsync(host, session, "Quick order");
        var orders = Assert.IsType<CarListTemplate>(session.Stack[^1]);
        await host.SelectAsync(orders.Sections[0].Items[0]);
        var details = Assert.IsType<CarInformationTemplate>(session.Stack[^1]);
        await host.SelectAsync(details.Actions[0]);
        var confirm = Assert.IsType<CarAlertTemplate>(session.Presented);
        Assert.Contains("$", confirm.Title!.Text, StringComparison.Ordinal);
        await host.SelectAsync(confirm.Actions[0]);

        Assert.Contains("Pickup", session.Presented?.Title?.Text, StringComparison.Ordinal);
        Assert.Equal(3, session.Stack.Count);
        Assert.Empty(host.Errors);
    }

    [Fact]
    public async Task Driving_task_flow_fits_depth_three()
    {
        var (host, session) = await ConnectAsync(CarAppCategory.DrivingTask, CarPlatform.CarPlay);
        using var _ = host;

        await OpenAsync(host, session, "Driver tasks");
        var grid = Assert.IsType<CarGridTemplate>(session.Stack[^1]);
        Assert.InRange(grid.Items.Count, 2, 8);
        foreach (var item in grid.Items)
        {
            await host.SelectAsync(item);
            var confirm = Assert.IsType<CarAlertTemplate>(session.Presented);
            await host.SelectAsync(confirm.Actions[0]);
            Assert.IsType<CarAlertTemplate>(session.Presented);
            await session.DismissAsync();
        }

        Assert.Equal(2, session.Stack.Count);
        Assert.Empty(host.Errors);
    }

    [Fact]
    public async Task Iot_flow_shows_a_message()
    {
        var (host, session) = await ConnectAsync(CarAppCategory.Iot, CarPlatform.AndroidAuto);
        using var _ = host;

        await OpenAsync(host, session, "Home");
        var grid = Assert.IsType<CarGridTemplate>(session.Stack[^1]);
        await host.SelectAsync(grid.Items[0]);
        var message = Assert.IsType<CarMessageTemplate>(session.Stack[^1]);
        Assert.Contains("Gate", message.Message.Text, StringComparison.Ordinal);
        await host.SelectAsync(message.Actions[0]);

        Assert.IsType<CarGridTemplate>(session.Stack[^1]);
        Assert.Empty(host.Errors);
    }

    [Fact]
    public async Task Weather_flow_shows_information_and_stations()
    {
        var (host, session) = await ConnectAsync(CarAppCategory.Weather, CarPlatform.AndroidAuto);
        using var _ = host;

        await OpenAsync(host, session, "Weather");
        var weather = Assert.IsType<CarInformationTemplate>(session.Stack[^1]);
        Assert.InRange(weather.Items.Count, 1, 4);
        await host.SelectAsync(weather.Actions[1]);
        Assert.Equal(weather.Id, session.Stack[^1].Id);
        await host.SelectAsync(((CarInformationTemplate)session.Stack[^1]).Actions[0]);
        var stations = Assert.IsType<CarPointOfInterestTemplate>(session.Stack[^1]);
        await host.SelectAsync(stations.Points[0]);

        Assert.IsType<CarMessageTemplate>(session.Stack[^1]);
        Assert.Equal(4, session.Stack.Count);
        Assert.Empty(host.Errors);
    }

    // Stage 9: the assistant state action buttons (CarPlay iOS 26.4): "End" closes the modal, and the modal stays after
    // "Repeat" is selected (a voice action button does not close the CPVoiceControlTemplate)
    [Fact]
    public async Task Voice_states_have_actions()
    {
        var (host, session) = await ConnectAsync(CarAppCategory.VoiceConversational, CarPlatform.CarPlay);
        using var _ = host;
        var voice = Assert.IsType<CarVoiceControlTemplate>(session.Presented);

        Assert.Equal(["End"], voice.States[0].Actions.Select(a => a.Title.Text));
        Assert.Equal(["Repeat", "End"], voice.States[2].Actions.Select(a => a.Title.Text));
        await host.SelectAsync(voice.States[0].Actions[0]);

        Assert.Null(session.Presented);
        Assert.Empty(host.Errors);
    }

    [Fact]
    public async Task Voice_assistant_starts_listening_and_walks_the_states()
    {
        var (host, session) = await ConnectAsync(CarAppCategory.VoiceConversational, CarPlatform.CarPlay);
        using var _ = host;

        // The conversational app starts with a voice interaction
        var voice = Assert.IsType<CarVoiceControlTemplate>(session.Presented);
        Assert.Equal("listening", voice.ActiveStateId);
        Assert.Equal(["listening", "thinking", "speaking"], voice.States.Select(s => s.Identifier));
        Assert.False(voice.States[0].AnimationImages.IsEmpty);
        var states = new List<string?>();
        session.TemplateDisappeared += (_, e) => states.Add("closed:" + e.Template.Id);

        // Conversation simulation: listens → thinks → speaks → close, every 1.5 s
        for (var i = 0; i < 100 && session.Presented is not null; i++)
        {
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        Assert.Null(session.Presented);
        Assert.Contains(host.Calls, c => c.Operation == FakeCarHostOperation.Update && c.Template is CarVoiceControlTemplate { ActiveStateId: "speaking" });
        Assert.Contains("closed:assistant", states);
        Assert.Empty(host.Errors);
    }
}
