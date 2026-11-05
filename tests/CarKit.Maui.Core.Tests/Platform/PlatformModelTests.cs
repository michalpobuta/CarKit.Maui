using Xunit;

namespace CarKit.Maui.Core.Tests.Platform;

// Stage 9: new platform feature models (playback state, list header, sports mode) and their elements in the model walk
public class PlatformModelTests
{
    [Fact]
    public void Playback_rejects_negative_times()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CarPlaybackConfiguration { Elapsed = TimeSpan.FromSeconds(-1) });
        Assert.Throws<ArgumentOutOfRangeException>(() => new CarPlaybackConfiguration { Duration = TimeSpan.FromSeconds(-1) });

        var playback = new CarPlaybackConfiguration { Elapsed = TimeSpan.FromSeconds(30), Duration = TimeSpan.FromMinutes(3) };
        Assert.Equal(CarPlaybackPresentation.None, playback.Presentation);
        Assert.Equal(CarPlaybackAction.None, playback.Action);
        Assert.Equal(playback, playback with { });
    }

    [Theory]
    [InlineData("Legends Wellington", "LEG")]
    [InlineData("FC", "FC")]
    [InlineData("1. FC Köln", "1FC")]
    public void Sports_team_initials_default_to_the_name(string name, string initials) =>
        Assert.Equal(initials, new CarSportsTeam(name).Initials);

    [Fact]
    public void Sports_team_keeps_explicit_initials_and_needs_a_name()
    {
        Assert.Equal("LW", new CarSportsTeam("Legends Wellington") { Initials = "LW" }.Initials);
        Assert.Throws<ArgumentException>(() => new CarSportsTeam(" "));
        Assert.Throws<ArgumentNullException>(() => new CarNowPlayingSports(null!, new CarSportsTeam("Lions")));
    }

    [Fact]
    public void Sports_clock_rejects_negative_time()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CarSportsClock(TimeSpan.FromSeconds(-1), countsUp: true));

        var clock = new CarSportsClock(TimeSpan.FromMinutes(45), countsUp: true) { IsPaused = true };
        Assert.True(clock.CountsUp);
        Assert.True(clock.IsPaused);
    }

    [Fact]
    public void Details_header_needs_a_thumbnail()
    {
        Assert.Throws<ArgumentNullException>(() => new CarListDetailsHeader(null!));
        Assert.False(new CarListDetailsHeader(CarImage.FromFile("a.png")).UsesAdaptiveBackground);
    }

    [Fact]
    public void Walker_finds_voice_and_header_actions()
    {
        var stop = new CarAction("Stop") { Id = "stop" };
        var go = new CarAction("Go") { Id = "go" };
        var voice = new CarVoiceControlTemplate([new CarVoiceControlState("s", "Listening") { Actions = [stop] }]);
        var list = new CarListTemplate("List") { DetailsHeader = new CarListDetailsHeader(CarImage.FromFile("a.png")) { Actions = [go] } };

        Assert.Same(stop, CarModelWalker.Find(voice, "stop"));
        Assert.Same(go, CarModelWalker.Find(list, "go"));
    }

    [Fact]
    public void Duplicate_ids_across_voice_states_are_reported()
    {
        var voice = new CarVoiceControlTemplate([
            new CarVoiceControlState("a", "A") { Actions = [new CarAction("Stop") { Id = "x" }] },
            new CarVoiceControlState("b", "B") { Actions = [new CarAction("Pause") { Id = "x" }] },
        ]);

        Assert.Throws<CarKitException>(() => Validation.V.Create(CarAppCategory.VoiceConversational, ios: new(26, 4)).Prepare(voice, null));
    }
}
