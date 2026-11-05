using CarKit.Maui.Core.Tests.Session;
using CarKit.Maui.Core.Tests.Validation;
using CarKit.Maui.Testing;
using Microsoft.Extensions.Logging;
using Xunit;

#pragma warning disable CARKIT0001 // iOS 27 models (MiniPlayer, overlay, video list) are experimental

namespace CarKit.Maui.Core.Tests.Platform;

// Stage 9: feature gate (spec: "New platform features decisions (Stage 9)", Feature gate) — rules × host asset, stripping
// unsupported features from the model sent to the host
public class FeatureGateTests
{
    private static readonly CarImage _thumbnail = CarImage.FromFile("cover.png");

    private static bool Newest(CarFeature feature) => CarFeatureDefaults.NewestAsset.Contains(feature);

    private static bool Ios26_0Asset(CarFeature feature) => feature == CarFeature.NowPlayingSportsMode;

    private static CarCapabilities CarPlay(Version ios, Func<CarFeature, bool>? asset = null) =>
        new(CarPlatform.CarPlay, ios, null, null, asset ?? Newest);

    private static CarListTemplate ListWithHeader() => new("Parking lots")
    {
        Id = "parkings",
        DetailsHeader = new CarListDetailsHeader(_thumbnail)
        {
            Title = "Downtown",
            Actions = [new CarAction("Navigate") { Id = "go" }],
            Playback = new CarPlaybackConfiguration { Presentation = CarPlaybackPresentation.Video },
        },
        Sections = [new CarSection([new CarListItem("P1") { Id = "p1", Playback = new CarPlaybackConfiguration { Action = CarPlaybackAction.Play } }])],
    };

    private static CarVoiceControlTemplate VoiceWithActions() => new([
        new CarVoiceControlState("listening", "Listening") { Actions = [new CarAction("Stop") { Id = "stop" }, new CarAction("Pause") { Id = "pause" }] },
    ]) { Id = "voice" };

    public static TheoryData<CarPlatform, CarFeature, string?> Rules => new()
    {
        { CarPlatform.CarPlay, CarFeature.VoiceControlActionButtons, "26.4" },
        { CarPlatform.CarPlay, CarFeature.ListDetailsHeader, "26.4" },
        { CarPlatform.CarPlay, CarFeature.PlaybackConfiguration, "26.4" },
        { CarPlatform.CarPlay, CarFeature.NowPlayingSportsMode, "18.4" },
        { CarPlatform.CarPlay, CarFeature.RouteSharing, "26.4" },
        { CarPlatform.CarPlay, CarFeature.VideoPlayback, "26.4" },
        { CarPlatform.CarPlay, CarFeature.NowPlayingMiniPlayerOptOut, "27.0" },
        { CarPlatform.CarPlay, CarFeature.VoiceControlOverlay, "27.0" },
        { CarPlatform.AndroidAuto, CarFeature.VoiceControlActionButtons, "1" },
        { CarPlatform.AndroidAuto, CarFeature.ListDetailsHeader, null },
        { CarPlatform.AndroidAuto, CarFeature.NowPlayingSportsMode, null },
        { CarPlatform.AndroidAuto, CarFeature.RouteSharing, null },
    };

    [Theory]
    [MemberData(nameof(Rules))]
    public void Feature_rules_match_the_spec(CarPlatform platform, CarFeature feature, string? minimum)
    {
        var rules = CarPlatformRules.For(platform);

        if (minimum is null)
        {
            Assert.False(rules.Features.ContainsKey(feature));
            return;
        }

        var requirement = rules.Features[feature];
        Assert.Equal(minimum, platform == CarPlatform.CarPlay ? requirement.MinIosVersion?.ToString() : requirement.MinApiLevel?.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Supports_needs_rules_and_asset()
    {
        Assert.True(CarPlay(new(26, 4)).Supports(CarFeature.ListDetailsHeader));
        Assert.False(CarPlay(new(26, 0)).Supports(CarFeature.ListDetailsHeader));
        Assert.False(CarPlay(new(26, 4), Ios26_0Asset).Supports(CarFeature.ListDetailsHeader));
        Assert.True(CarPlay(new(26, 0), Ios26_0Asset).Supports(CarFeature.NowPlayingSportsMode));
        Assert.False(CarPlay(new(18, 3)).Supports(CarFeature.NowPlayingSportsMode));
        // iOS 27 will not help without the 27.0 asset (Xcode 27)
        Assert.False(CarPlay(new(27, 0)).Supports(CarFeature.NowPlayingMiniPlayerOptOut));
        Assert.True(CarPlay(new(27, 0), _ => true).Supports(CarFeature.NowPlayingMiniPlayerOptOut));
        Assert.True(new CarCapabilities(CarPlatform.AndroidAuto, null, 1).Supports(CarFeature.VoiceControlActionButtons));
        Assert.False(new CarCapabilities(CarPlatform.AndroidAuto, null, 8).Supports(CarFeature.ListDetailsHeader));
        // An interface without an override (an app with its own ICarCapabilities) reports no features
        Assert.False(((ICarCapabilities)new MinimalCapabilities()).Supports(CarFeature.ListDetailsHeader));
    }

    [Fact]
    public void Feature_gate_strips_the_list_header_on_ios_26_0()
    {
        var list = ListWithHeader();

        var sent = Assert.IsType<CarListTemplate>(V.Create(CarAppCategory.Parking, ios: new(26, 0)).Prepare(list, 1));

        Assert.Null(sent.DetailsHeader);
        Assert.Null(Assert.IsType<CarListItem>(sent.Sections[0].Items[0]).Playback);
        Assert.Equal(list.Id, sent.Id);
        Assert.NotNull(list.DetailsHeader);
    }

    [Fact]
    public void Feature_gate_strips_voice_actions_without_the_asset()
    {
        var voice = VoiceWithActions();

        var sent = Assert.IsType<CarVoiceControlTemplate>(
            V.Create(CarAppCategory.VoiceConversational, ios: new(26, 4), assetFeatures: Ios26_0Asset).Prepare(voice, null));

        Assert.True(sent.States[0].Actions.IsEmpty);
        Assert.Equal("listening", sent.States[0].Identifier);
    }

    [Fact]
    public void Feature_gate_keeps_features_when_supported()
    {
        var list = ListWithHeader();
        var voice = VoiceWithActions();

        Assert.Same(list, V.Create(CarAppCategory.Parking, ios: new(26, 4)).Prepare(list, 1));
        Assert.Same(voice, V.Create(CarAppCategory.VoiceConversational, ios: new(26, 4)).Prepare(voice, null));
    }

    [Fact]
    public void Feature_gate_logs_unavailable_features()
    {
        var logger = new TestLogger();

        V.Create(CarAppCategory.Parking, ios: new(26, 0), logger: logger).Prepare(ListWithHeader(), 1);

        var entry = Assert.Single(logger.Entries, e => e.Message.Contains("ListDetailsHeader", StringComparison.Ordinal));
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Contains(logger.Entries, e => e.Message.Contains("PlaybackConfiguration", StringComparison.Ordinal));
    }

    [Fact]
    public void Feature_gate_runs_in_off_mode()
    {
        var validator = V.Create(CarAppCategory.Parking, ios: new(26, 0), configure: o => o.Validation = CarValidationMode.Off);

        Assert.Null(Assert.IsType<CarListTemplate>(validator.Prepare(ListWithHeader(), 1)).DetailsHeader);
    }

    [Fact]
    public void Feature_gate_reaches_tab_children()
    {
        var tabs = new CarTabTemplate([ListWithHeader() with { TabInfo = new CarTabInfo("Parking lots") }]);

        var sent = Assert.IsType<CarTabTemplate>(V.Create(CarAppCategory.Parking, ios: new(26, 0)).Prepare(tabs, 1));

        Assert.Null(Assert.IsType<CarListTemplate>(sent.Tabs[0]).DetailsHeader);
    }

    [Fact]
    public void Overlay_and_mini_player_fall_back_on_every_asset()
    {
        var logger = new TestLogger();
        var voice = VoiceWithActions() with { IsOverlay = true };
        var nowPlaying = new CarNowPlayingTemplate { AllowsMiniPlayer = false };

        var sentVoice = Assert.IsType<CarVoiceControlTemplate>(V.Create(CarAppCategory.Audio, ios: new(27, 0), logger: logger).Prepare(voice, null));
        var sentNowPlaying = Assert.IsType<CarNowPlayingTemplate>(V.Create(CarAppCategory.Audio, ios: new(27, 0), logger: logger).Prepare(nowPlaying, 1));

        Assert.False(sentVoice.IsOverlay);
        Assert.False(sentVoice.States[0].Actions.IsEmpty);
        // The system shows the MiniPlayer; the flag has nothing to turn off without the 27.0 asset, so it stays and
        // goes to the log
        Assert.False(sentNowPlaying.AllowsMiniPlayer);
        Assert.Contains(logger.Entries, e => e.Message.Contains("VoiceControlOverlay", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, e => e.Message.Contains("NowPlayingMiniPlayerOptOut", StringComparison.Ordinal));
    }

    [Fact]
    public void Sports_mode_is_stripped_below_ios_18_4_and_on_android_auto()
    {
        var sports = new CarNowPlayingTemplate { Sports = new CarNowPlayingSports(new CarSportsTeam("Legends"), new CarSportsTeam("Lions")) };

        Assert.Null(Assert.IsType<CarNowPlayingTemplate>(V.Create(CarAppCategory.Audio, ios: new(18, 3)).Prepare(sports, 1)).Sports);
        Assert.NotNull(Assert.IsType<CarNowPlayingTemplate>(V.Create(CarAppCategory.Audio, ios: new(26, 0), assetFeatures: Ios26_0Asset).Prepare(sports, 1)).Sports);
        Assert.Null(Assert.IsType<CarNowPlayingTemplate>(V.Create(CarAppCategory.Audio, CarPlatform.AndroidAuto).Prepare(sports, 1)).Sports);
    }

    [Fact]
    public void Video_browse_needs_ios_27_and_the_video_category()
    {
        var video = new CarPlayVideoBrowseTemplate("Movies") { DetailsHeader = new CarListDetailsHeader(_thumbnail) { Title = "What's new" } };

        var sent = Assert.IsType<CarPlayVideoBrowseTemplate>(V.Create(CarAppCategory.Video, ios: new(27, 0)).Prepare(video, 1));
        Assert.NotNull(sent.DetailsHeader);
        Assert.Throws<CarTemplateNotSupportedException>(() => V.Create(CarAppCategory.Video, ios: new(26, 4)).Prepare(video, 1));
        Assert.Throws<CarTemplateNotAllowedException>(() => V.Create(CarAppCategory.Audio, ios: new(27, 0)).Prepare(video, 1));
        Assert.IsType<CarListTemplate>(V.Create(CarAppCategory.Video, ios: new(26, 4)).Prepare(video.WithFallback(new CarListTemplate("Movies")), 1));
    }

    [Fact]
    public void Voice_actions_become_message_actions_on_android_auto()
    {
        var sent = Assert.IsType<CarMessageTemplate>(V.Create(CarAppCategory.Communication, CarPlatform.AndroidAuto).Prepare(VoiceWithActions(), null));

        Assert.Equal(["stop", "pause"], sent.Actions.Select(a => a.Id));
    }

    [Fact]
    public void Voice_state_actions_are_limited_to_two()
    {
        var voice = new CarVoiceControlTemplate([new CarVoiceControlState("s", "Listening") { Actions = [new CarAction("A"), new CarAction("B"), new CarAction("C")] }]);

        var sent = Assert.IsType<CarVoiceControlTemplate>(V.Create(CarAppCategory.VoiceConversational, ios: new(26, 4)).Prepare(voice, null));

        Assert.Equal(2, sent.States[0].Actions.Count);
        Assert.Throws<CarTemplateLimitException>(() => V.Create(
            CarAppCategory.VoiceConversational, ios: new(26, 4), configure: o => o.Limits.OnOverflow = CarOverflowBehavior.Throw).Prepare(voice, null));
    }

    [Fact]
    public async Task Voice_action_keeps_the_modal_on_carplay()
    {
        var selected = new List<string>();
        var voice = new CarVoiceControlTemplate([
            new CarVoiceControlState("listening", "Listening") { Actions = [new CarAction("Stop", _ => { selected.Add("stop"); return Task.CompletedTask; }) { Id = "stop" }] },
        ]) { Id = "voice" };
        using var carPlay = new FakeCarHost(CarAppCategory.VoiceConversational);
        var session = await carPlay.ConnectAsync(new TestHandler(async (s, _) =>
        {
            await s.SetRootAsync(new CarListTemplate("Assistant"));
            await s.PresentAsync(voice);
        }));
        using var androidAuto = new FakeCarHost(CarAppCategory.Communication, CarPlatform.AndroidAuto);
        var androidSession = await androidAuto.ConnectAsync(new TestHandler(async (s, _) =>
        {
            await s.SetRootAsync(new CarListTemplate("Conversations"));
            await s.PresentAsync(voice);
        }));

        await carPlay.SelectAsync(voice.States[0].Actions[0]);
        await androidAuto.SelectAsync(voice.States[0].Actions[0]);

        Assert.Equal(["stop", "stop"], selected);
        Assert.Same(voice, session.Presented);
        // Android Auto: the fallback (MessageTemplate screen) closes after an action like an alert screen (Stage 3)
        Assert.Null(androidSession.Presented);
    }

    [Fact]
    public async Task Fake_host_features_simulate_an_older_asset()
    {
        using var host = new FakeCarHost(CarAppCategory.Parking);
        host.Features.Remove(CarFeature.ListDetailsHeader);
        var session = await host.ConnectAsync(new TestHandler((s, _) => s.SetRootAsync(ListWithHeader())));

        Assert.False(session.Capabilities.Supports(CarFeature.ListDetailsHeader));
        Assert.True(session.Capabilities.Supports(CarFeature.VoiceControlActionButtons));
        Assert.Null(Assert.IsType<CarListTemplate>(host.Calls[^1].Template).DetailsHeader);
        Assert.NotNull(Assert.IsType<CarListTemplate>(session.Stack[0]).DetailsHeader);
        Assert.DoesNotContain(CarFeature.NowPlayingMiniPlayerOptOut, new FakeCarHost(CarAppCategory.Audio).Features);
    }

    private sealed class MinimalCapabilities : ICarCapabilities
    {
        public CarPlatform Platform => CarPlatform.CarPlay;

        public Version? IosVersion => new(26, 4);

        public int? CarAppApiLevel => null;

        public bool Supports(CarTemplateKind kind) => true;

        public int? GetLimit(CarLimitKind kind) => null;
    }
}
