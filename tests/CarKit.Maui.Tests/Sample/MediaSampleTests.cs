using CarKit.Maui.Media;
using CarKit.Maui.Testing;
using CarKit.Maui.Tests.Media;
using CarKit.Sample.Media;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CarKit.Maui.Tests.Sample;

// Media sample (Stage 5 acceptance): a library with ≥ 3 levels, search, shuffle/repeat/rate; auto-browse walks the
// library on FakeCarHost (Audio, Strict) on CarPlay and Android Auto
public class MediaSampleTests
{
    public static TheoryData<CarPlatform> Platforms => [CarPlatform.CarPlay, CarPlatform.AndroidAuto];

    private sealed class FakeOutput : ISampleAudioOutput
    {
        public List<string> Calls { get; } = [];

        public TimeSpan Position { get; set; }

        public bool IsPlaying { get; private set; }

        public event EventHandler? Completed;

        public void Load(string asset) => Calls.Add("Load:" + asset);

        public void Play()
        {
            IsPlaying = true;
            Calls.Add("Play");
        }

        public void Pause()
        {
            IsPlaying = false;
            Calls.Add("Pause");
        }

        public void Seek(TimeSpan position) => Position = position;

        public void SetRate(float rate) => Calls.Add("Rate:" + rate.ToString(System.Globalization.CultureInfo.InvariantCulture));

        public void Finish() => Completed?.Invoke(this, EventArgs.Empty);
    }

    private static (SampleMediaPlayer Player, FakeOutput Output, SampleMediaLibrary Library) Player()
    {
        var library = new SampleMediaLibrary();
        var output = new FakeOutput();
        return (new SampleMediaPlayer(library, output, new InlineDispatcher(), NullLogger.Instance, new Random(7)), output, library);
    }

    [Fact]
    public async Task Library_is_at_least_three_levels_deep()
    {
        var library = new SampleMediaLibrary();
        var root = await library.GetRootAsync(TestContext.Current.CancellationToken);

        // Root → Genres → Ambient → Tone Quartet → Morning → tracks: playable leaves 5 levels under the root
        string[] path = ["genres", "genre-ambient", "artist-quartet", "morning"];
        var parent = root.Id;
        foreach (var id in path)
        {
            var children = await library.GetChildrenAsync(parent, 0, 50, TestContext.Current.CancellationToken);
            var next = Assert.Single(children, c => c.Id == id);
            Assert.True(next.IsBrowsable);
            parent = next.Id;
        }

        var tracks = await library.GetChildrenAsync(parent, 0, 50, TestContext.Current.CancellationToken);
        Assert.Equal(4, tracks.Count);
        Assert.All(tracks, t => Assert.True(t.IsPlayable && t.Artwork is not null && t.Duration == SampleMediaLibrary.ToneDuration));

        // A long playlist for paging
        Assert.Equal(20, (await library.GetChildrenAsync("playlist-long", 1, 20, TestContext.Current.CancellationToken)).Count);
        Assert.Equal(5, (await library.GetChildrenAsync("playlist-long", 2, 20, TestContext.Current.CancellationToken)).Count);
    }

    // Stage 9: a match broadcast in the playlists. The CarPlay sports mode (iOS 18.4) follows the playing item
    [Fact]
    public async Task Live_match_uses_the_sports_mode()
    {
        var library = new SampleMediaLibrary();

        var live = await library.GetChildrenAsync("playlist-live", 0, 10, TestContext.Current.CancellationToken);

        var match = Assert.Single(live);
        Assert.True(match.IsPlayable);
        var sports = Assert.IsType<CarNowPlayingSports>(match.Sports);
        Assert.Equal("Red Lions", sports.LeftTeam.Name);
        Assert.Equal("Blue Hawks", sports.RightTeam.Name);
        Assert.NotNull(sports.Clock);
        Assert.Contains(await library.GetChildrenAsync("playlists", 0, 10, TestContext.Current.CancellationToken), item => item.Id == "playlist-live");
        Assert.All(library.ChildrenOf("morning"), track => Assert.Null(track.Sports));
    }

    [Fact]
    public async Task Search_finds_tracks_by_title_and_artist()
    {
        var library = new SampleMediaLibrary();

        var byTitle = await library.SearchAsync("neon", TestContext.Current.CancellationToken);
        var byArtist = await library.SearchAsync("duet", TestContext.Current.CancellationToken);

        Assert.Equal(["city-2"], byTitle.Select(t => t.Id));
        Assert.Equal(4, byArtist.Count);
        Assert.Empty(await library.SearchAsync("  ", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Sample_player_shuffles_repeats_and_changes_rate()
    {
        var (player, output, _) = Player();

        await player.PlayAsync("night-2");
        Assert.Equal(CarPlaybackStatus.Playing, player.Current.Status);
        Assert.Equal("night-2", player.Current.Item?.Id);
        Assert.Equal(4, player.Current.Queue.Count);
        Assert.Equal(1, player.Current.QueueIndex);
        Assert.Contains("Load:tones/tone_c.wav", output.Calls);

        await player.SkipNextAsync();
        Assert.Equal("night-3", player.Current.Item?.Id);

        await player.SetShuffleAsync(true);
        Assert.True(player.Current.IsShuffleEnabled);
        Assert.Equal("night-3", player.Current.Item?.Id);
        Assert.Equal(0, player.Current.QueueIndex);
        await player.SetShuffleAsync(false);
        Assert.Equal(2, player.Current.QueueIndex);

        await player.SetRepeatAsync(CarRepeatMode.One);
        output.Finish();
        Assert.Equal("night-3", player.Current.Item?.Id);

        await player.SkipNextAsync();
        await player.SetRepeatAsync(CarRepeatMode.All);
        output.Finish();
        Assert.Equal("night-1", player.Current.Item?.Id);

        await player.PlayAsync("night-4");
        await player.SetRepeatAsync(CarRepeatMode.Off);
        output.Finish();
        Assert.Equal(CarPlaybackStatus.Ended, player.Current.Status);

        await player.SetRateAsync(1.5f);
        Assert.Equal(1.5f, player.Current.Rate);
        Assert.Equal("Rate:1.5", output.Calls[^1]);

        await player.PauseAsync();
        await player.ResumeAsync();
        Assert.Equal(CarPlaybackStatus.Playing, player.Current.Status);
        await player.InvokeCustomActionAsync("like");
        Assert.Contains("night-4", player.Liked);
    }

    [Theory]
    [MemberData(nameof(Platforms))]
    public async Task Auto_browse_walks_the_sample_library(CarPlatform platform)
    {
        var (player, _, library) = Player();
        var errors = new RecordingErrors();
        var options = TestMediaFactory.Options(1f, 1.25f, 1.5f, 2f) with { NowPlaying = SampleNowPlaying.Template };
        var controller = new CarMediaController(player, options, errors, new InlineDispatcher(), TimeProvider.System);
        var runtime = new CarMediaRuntime(library, controller, options, errors, NullLogger.Instance);
        using var host = new FakeCarHost(CarAppCategory.Audio, platform);
        host.Options.Validation = CarValidationMode.Strict;
        var session = await host.ConnectAsync(new CarMediaAutoBrowseHandler(runtime));

        var tabs = Assert.IsType<CarTabTemplate>(session.Stack[0]);
        Assert.Equal(["Albums", "Genres", "Playlists"], tabs.Tabs.Select(t => t.TabInfo!.Title.Text));
        foreach (var text in new[] { "Ambient", "Tone Quartet", "Morning", "Coffee" })
        {
            var list = (CarListTemplate)(session.Stack[^1] is CarTabTemplate root ? root.Tabs[1] : session.Stack[^1]);
            await host.SelectAsync(list.Sections.SelectMany(s => s.Items).OfType<CarListItem>().First(i => i.Text.Text == text));
        }

        Assert.Equal("morning-2", player.Current.Item?.Id);
        if (platform == CarPlatform.CarPlay)
        {
            // Depth 5 (tabs + 3 lists + Now Playing) leaves no room for Up Next: auto-browse returns to the root
            var nowPlaying = Assert.IsType<CarNowPlayingTemplate>(session.Stack[^1]);
            Assert.Equal(2, session.Stack.Count);
            var car = (CarSession)session;
            await car.InvokeAsync(nowPlaying.OnUpNextSelected, car.CreateContext(nowPlaying), "UpNext");
            Assert.Equal("Up Next", Assert.IsType<CarListTemplate>(session.Stack[^1]).Title!.Text);
        }
        else
        {
            // Android Auto without media templates: the Media3 service shows playback, the stack has no Now Playing
            Assert.IsType<CarListTemplate>(session.Stack[^1]);
            Assert.Equal(4, session.Stack.Count);
        }

        Assert.Empty(host.Errors);
        Assert.Empty(errors.Errors);
    }
}
