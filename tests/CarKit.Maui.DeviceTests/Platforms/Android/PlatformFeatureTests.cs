using Android.OS;
using Android.Runtime;
using AndroidX.Car.App;
using AndroidX.Car.App.Model;
using AndroidX.Car.App.Serialization;
using CarKit.Maui.Navigation;
using CarKit.Maui.Renderers;
using NativeAction = AndroidX.Car.App.Model.Action;
using NativeCarText = AndroidX.Car.App.Model.CarText;

namespace CarKit.Maui.DeviceTests;

// Stage 9: new platform features on Car App Library 1.7.0.1 (spec: "New platform features decisions (Stage 9)"): voice
// state action buttons in the MessageTemplate fallback (every API level), a list header with no equivalent (gate), a Trip
// with the destinations of route segments
internal static class PlatformFeatureTests
{
    private static readonly Handler _main = new(Looper.MainLooper!);

    public static IReadOnlyList<DeviceTest> All() =>
    [
        new(nameof(Voice_fallback_shows_state_actions), Voice_fallback_shows_state_actions),
        new(nameof(List_header_is_left_out_on_android_auto), List_header_is_left_out_on_android_auto),
        new(nameof(Trip_destinations_from_segments), Trip_destinations_from_segments),
    ];

    private sealed class Fixture(int apiLevel = 8, CarAppCategory categories = CarAppCategory.Communication)
    {
        public TestSession Test { get; } = TestSession.AndroidAuto(apiLevel, categories);

        public CarAppRenderContext Context => field ??= new CarAppRenderContext(
            Test.Session, Test, apiLevel, Android.App.Application.Context,
            new CarImageLoader<CarIcon>(new CarAppImages(Android.App.Application.Context), Test, combineDarkVariants: false),
            () => CarContentStyle.Light, work => _main.Post(work), useSectionedItemTemplate: false, Test);

        public CarAppRendered Render(CarTemplate template, CarAppScreenState screen) => CarAppTemplateRenderers.Render(template, Context, screen);
    }

    private sealed class Done : Java.Lang.Object, IOnDoneCallback
    {
        public void OnFailure(Bundleable? response)
        {
        }

        public void OnSuccess(Bundleable? response)
        {
        }
    }

    private static string? Text(NativeCarText? text) => text?.ToCharSequence();

    private static async Task Voice_fallback_shows_state_actions()
    {
        var f = new Fixture(apiLevel: 1);
        var selected = new List<string>();
        var voice = new CarVoiceControlTemplate([
            new CarVoiceControlState("listening", "Listening")
            {
                Actions =
                [
                    new CarAction("Stop", _ => { selected.Add("stop"); return Task.CompletedTask; }) { Id = "stop" },
                    new CarAction("Pause") { Id = "pause" },
                ],
            },
        ]) { Id = "voice", Title = "Assistant" };
        await f.Test.Session.SetRootAsync(new CarListTemplate("Conversations"));

        await f.Test.Session.PresentAsync(voice);

        Check.True(f.Test.Session.Capabilities.Supports(CarFeature.VoiceControlActionButtons));
        var sent = Check.Is<CarMessageTemplate>(f.Test.Templates[^1]);
        Check.Equal(2, sent.Actions.Count);
        var closed = 0;
        var screen = new CarAppScreenState { IsModal = true, CloseModal = () => { closed++; return Task.CompletedTask; } };
        var native = Check.Is<MessageTemplate>(f.Render(sent, screen).Template);
        Check.Equal(2, native.Actions!.Count);
        Check.Equal("Stop", Text(((IJavaObject)native.Actions[0]).JavaCast<NativeAction>()!.Title));
        ((IJavaObject)native.Actions[0]).JavaCast<NativeAction>()!.OnClickDelegate!.SendClick(new Done());
        await Check.Eventually(() => selected.Count == 1, "voice action handler");
        Check.Equal(1, closed);
    }

    private static async Task List_header_is_left_out_on_android_auto()
    {
        var f = new Fixture(categories: CarAppCategory.Parking);
        var list = new CarListTemplate("Parking lots")
        {
            DetailsHeader = new CarListDetailsHeader(CarImage.FromFile("dotnet_bot.png")) { Title = "Downtown" },
            Sections = [new CarSection([new CarListItem("P1") { Playback = new CarPlaybackConfiguration { Action = CarPlaybackAction.Play } }])],
        };

        await f.Test.Session.SetRootAsync(list);

        var sent = Check.Is<CarListTemplate>(f.Test.Templates[^1]);
        Check.Null(sent.DetailsHeader);
        Check.Null(Check.Is<CarListItem>(sent.Sections[0].Items[0]).Playback);
        Check.Is<ListTemplate>(f.Render(sent, new CarAppScreenState { IsRoot = true }).Template);
    }

    private static async Task Trip_destinations_from_segments()
    {
        await NavigationTestServices.ResetAsync();
        var f = new Fixture(categories: CarAppCategory.Navigation);
        var charger = new CarWaypoint(new CarLocation(52.20, 21.00)) { Name = "Charger", Address = "1 Pine Street" };
        var route = new CarRouteChoice("Via the charger")
        {
            Id = "stop",
            Estimates = new CarTravelEstimates(new CarDistance(12, CarDistanceUnit.Kilometers), TimeSpan.FromMinutes(25)),
            Maneuvers = NavigationTestServices.Trip().Routes[0].Maneuvers,
            Segments =
            [
                new CarRouteSegment(new CarWaypoint(new CarLocation(52.2297, 21.0122)), charger) { Estimates = new CarTravelEstimates(new CarDistance(5, CarDistanceUnit.Kilometers), TimeSpan.FromMinutes(10)) },
                new CarRouteSegment(charger, new CarWaypoint(new CarLocation(52.1672, 20.9679)) { Name = "Work" }),
            ],
        };
        var trip = new CarTrip(new CarLocation(52.2297, 21.0122), new CarLocation(52.1672, 20.9679), [route]) { Id = "stop-trip", DestinationName = "Work" };
        await NavigationTestServices.Navigation.StartTripAsync(trip, route);
        var icons = CarAppIcons.For(f.Context, new CarAppScreenState { IsRoot = true });

        var native = CarAppNavigationModels.Trip(NavigationTestServices.Navigation.Snapshot, icons, [], DateTimeOffset.Now);
        await NavigationTestServices.Navigation.UpdateCurrentSegmentAsync(1);
        var afterStop = CarAppNavigationModels.Trip(NavigationTestServices.Navigation.Snapshot, icons, [], DateTimeOffset.Now);

        Check.Equal(2, native.Destinations!.Count);
        Check.Equal("Charger", Text(native.Destinations[0].Name));
        Check.Equal("1 Pine Street", Text(native.Destinations[0].Address));
        Check.Equal("Work", Text(native.Destinations[1].Name));
        Check.Equal(5d, native.DestinationTravelEstimates![0].RemainingDistance!.DisplayDistance);
        Check.Equal(12d, native.DestinationTravelEstimates[1].RemainingDistance!.DisplayDistance);
        Check.Equal(1, afterStop.Destinations!.Count);
        Check.Equal("Work", Text(afterStop.Destinations[0].Name));
        await NavigationTestServices.ResetAsync();
    }
}
