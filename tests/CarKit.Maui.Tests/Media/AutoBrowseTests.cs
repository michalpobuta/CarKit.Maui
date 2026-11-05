using CarKit.Maui.Media;
using CarKit.Maui.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CarKit.Maui.Tests.Media;

// media.UseAutoBrowse() on FakeCarHost (Audio category, Strict): CarPlay and Android Auto with templates
public class AutoBrowseTests
{
    public static TheoryData<CarPlatform> Platforms => [CarPlatform.CarPlay, CarPlatform.AndroidAuto];

    private sealed record Setup(FakeCarHost Host, ICarSession Session, TestMediaLibrary Library, TestMediaPlayer Player, RecordingErrors Errors) : IDisposable
    {
        public void Dispose() => Host.Dispose();

        public CarTemplate Top => Session.Stack[^1];
    }

    private static CarListItem Row(CarTemplate template, string text) =>
        template is CarListTemplate list
            ? list.Sections.SelectMany(s => s.Items).OfType<CarListItem>().Single(i => i.Text.Text == text)
            : throw new InvalidOperationException($"{template.Kind} is not a list");

    private static TestMediaLibrary Library()
    {
        var library = new TestMediaLibrary();
        library.Add("root", TestMediaLibrary.Folder("albums", "Albums"), TestMediaLibrary.Folder("genres", "Genres"), TestMediaLibrary.Folder("lists", "Playlists"));
        library.Add("albums", TestMediaLibrary.Folder("album1", "Album 1") with { Artwork = CarImage.FromFile("cover.png") });
        library.Add("album1", TestMediaLibrary.Track("t1", "First"), TestMediaLibrary.Track("t2", "Second") with { IsExplicit = true, Progress = 0.5 });
        library.Add("genres", TestMediaLibrary.Folder("jazz", "Jazz"));
        library.Add("lists", TestMediaLibrary.Track("t3", "Third"));
        return library;
    }

    private static async Task<Setup> ConnectAsync(
        CarPlatform platform,
        TestMediaLibrary? library = null,
        CarNowPlayingTemplate? nowPlaying = null,
        Action<FakeCarHost>? configure = null,
        bool yieldLibrary = false,
        bool templatedMedia = true)
    {
        library ??= Library();
        library.Yield = yieldLibrary;
        var player = new TestMediaPlayer();
        var errors = new RecordingErrors();
        var options = TestMediaFactory.Options() with { NowPlaying = nowPlaying ?? CarNowPlayingButtons.Default([1f]), TemplatedMedia = templatedMedia };
        var runtime = new CarMediaRuntime(library, new CarMediaController(player, options, errors, new InlineDispatcher(), TimeProvider.System), options, errors, NullLogger.Instance);
        var host = new FakeCarHost(CarAppCategory.Audio, platform);
        host.Options.Validation = CarValidationMode.Strict;
        configure?.Invoke(host);
        var session = await host.ConnectAsync(new CarMediaAutoBrowseHandler(runtime));
        return new Setup(host, session, library, player, errors);
    }

    [Theory]
    [MemberData(nameof(Platforms))]
    public async Task Browsable_root_children_become_tabs(CarPlatform platform)
    {
        using var setup = await ConnectAsync(platform);

        var tabs = Assert.IsType<CarTabTemplate>(Assert.Single(setup.Session.Stack));
        Assert.Equal(["Albums", "Genres", "Playlists"], tabs.Tabs.Select(t => t.TabInfo!.Title.Text));
        var albums = Assert.IsType<CarListTemplate>(tabs.Tabs[0]);
        Assert.Equal("Album 1", Assert.Single(albums.Sections.Single().Items.OfType<CarListItem>()).Text.Text);
        Assert.Empty(setup.Errors.Errors);
        Assert.Empty(setup.Host.Errors);
    }

    [Theory]
    [MemberData(nameof(Platforms))]
    public async Task Mixed_root_children_become_one_list(CarPlatform platform)
    {
        var library = new TestMediaLibrary();
        library.Add("root", TestMediaLibrary.Folder("albums", "Albums"), TestMediaLibrary.Track("t1", "Track"));

        using var setup = await ConnectAsync(platform, library);

        var list = Assert.IsType<CarListTemplate>(Assert.Single(setup.Session.Stack));
        Assert.Equal("Library", list.Title!.Text);
        Assert.True(Row(list, "Albums").IsBrowsable);
        Assert.False(Row(list, "Track").IsBrowsable);
    }

    [Theory]
    [MemberData(nameof(Platforms))]
    public async Task Selecting_a_folder_pushes_its_children(CarPlatform platform)
    {
        using var setup = await ConnectAsync(platform);
        var albums = ((CarTabTemplate)setup.Top).Tabs[0];

        await setup.Host.SelectAsync(Row(albums, "Album 1"));

        var tracks = Assert.IsType<CarListTemplate>(setup.Top);
        Assert.Equal("Album 1", tracks.Title!.Text);
        Assert.Equal(["First", "Second"], tracks.Sections.Single().Items.OfType<CarListItem>().Select(i => i.Text.Text));
        Assert.Contains("Children:album1:0:" + (platform == CarPlatform.CarPlay ? 11 : 5), setup.Library.Calls);
    }

    [Theory]
    [MemberData(nameof(Platforms))]
    public async Task Selecting_a_track_plays_it_and_pushes_now_playing_once(CarPlatform platform)
    {
        using var setup = await ConnectAsync(platform);
        await setup.Host.SelectAsync(Row(((CarTabTemplate)setup.Top).Tabs[0], "Album 1"));
        var album = setup.Top;

        await setup.Host.SelectAsync(Row(album, "First"));

        Assert.Equal(["Play:t1"], setup.Player.Calls);
        Assert.IsType<CarNowPlayingTemplate>(setup.Top);
        Assert.Equal(3, setup.Session.Stack.Count);

        await setup.Host.PressBackAsync();
        await setup.Host.SelectAsync(Row(album, "Second"));

        Assert.Equal(["Play:t1", "Play:t2"], setup.Player.Calls);
        Assert.Single(setup.Session.Stack.OfType<CarNowPlayingTemplate>());
        Assert.Equal(3, setup.Session.Stack.Count);
    }

    [Theory]
    [MemberData(nameof(Platforms))]
    public async Task Full_page_ends_with_more_and_more_appends_the_next_page(CarPlatform platform)
    {
        var library = new TestMediaLibrary();
        library.Add("root", [.. Enumerable.Range(0, 30).Select(i => TestMediaLibrary.Track("t" + i, "Track " + i))]);

        using var setup = await ConnectAsync(platform, library, configure: host => host.SetLimit(CarLimitKind.ListItems, 41));

        var first = Assert.IsType<CarListTemplate>(setup.Top);
        var rows = first.Sections.Single().Items.OfType<CarListItem>().ToArray();
        Assert.Equal(21, rows.Length);
        Assert.Equal(CarMediaAutoBrowseHandler.MoreText, rows[^1].Text.Text);

        await setup.Host.SelectAsync(rows[^1]);

        var updated = Assert.IsType<CarListTemplate>(setup.Top);
        Assert.Equal(first.Id, updated.Id);
        var all = updated.Sections.Single().Items.OfType<CarListItem>().Select(i => i.Text.Text).ToArray();
        Assert.Equal(30, all.Length);
        Assert.Equal("Track 29", all[^1]);
        Assert.Contains("Children:root:1:20", setup.Library.Calls);
        Assert.Contains(setup.Host.Calls, c => c.Operation == FakeCarHostOperation.Update);
    }

    [Theory]
    [MemberData(nameof(Platforms))]
    public async Task Items_map_artwork_progress_explicit_and_playing(CarPlatform platform)
    {
        using var setup = await ConnectAsync(platform);
        var albums = ((CarTabTemplate)setup.Top).Tabs[0];
        Assert.Equal(CarImage.FromFile("cover.png"), Row(albums, "Album 1").Image);

        setup.Player.Emit(TestMediaFactory.Playing("t1"));
        await setup.Host.SelectAsync(Row(albums, "Album 1"));

        var first = Row(setup.Top, "First");
        var second = Row(setup.Top, "Second");
        Assert.True(first.IsPlaying);
        Assert.Equal("Artist", first.Detail?.Text);
        Assert.False(second.IsPlaying);
        Assert.True(second.IsExplicitContent);
        Assert.Equal(0.5, second.PlaybackProgress);
    }

    [Theory]
    [MemberData(nameof(Platforms))]
    public async Task Library_failure_shows_unavailable_and_reports(CarPlatform platform)
    {
        var library = Library();
        library.FailWith = new IOException("offline");

        using var setup = await ConnectAsync(platform, library);

        var list = Assert.IsType<CarListTemplate>(Assert.Single(setup.Session.Stack));
        Assert.Equal(CarMediaAutoBrowseHandler.UnavailableText, list.EmptyViewTitle?.Text);
        Assert.Equal("Media.Library", Assert.Single(setup.Errors.Errors).Context.Operation);
    }

    [Theory]
    [MemberData(nameof(Platforms))]
    public async Task Up_next_lists_the_queue_and_plays_the_selection(CarPlatform platform)
    {
        using var setup = await ConnectAsync(platform, nowPlaying: CarNowPlayingButtons.Default([1f]) with { IsUpNextButtonEnabled = true });
        await setup.Host.SelectAsync(Row(((CarTabTemplate)setup.Top).Tabs[2], "Third"));
        var nowPlaying = Assert.IsType<CarNowPlayingTemplate>(setup.Top);
        setup.Player.Emit(TestMediaFactory.Playing("t3") with
        {
            Queue = [TestMediaLibrary.Track("t3", "Third"), TestMediaLibrary.Track("t4", "Fourth")],
            QueueIndex = 0,
        });

        var session = (CarSession)setup.Session;
        await session.InvokeAsync(nowPlaying.OnUpNextSelected, session.CreateContext(nowPlaying), "UpNext");

        var queue = Assert.IsType<CarListTemplate>(setup.Top);
        Assert.Equal(CarMediaAutoBrowseHandler.UpNextText, queue.Title!.Text);
        Assert.True(Row(queue, "Third").IsPlaying);
        await setup.Host.SelectAsync(Row(queue, "Fourth"));

        Assert.Equal(["Play:t3", "Play:t4"], setup.Player.Calls);
        Assert.IsType<CarNowPlayingTemplate>(setup.Top);
    }

    // Found in the iOS device test: the same track in two tabs (Tracks and Playlists) produced two rows
    // with the same Id in one CarTabTemplate → a validator error (Strict) and no screen
    [Theory]
    [MemberData(nameof(Platforms))]
    public async Task Same_item_in_two_tabs_gets_distinct_row_ids(CarPlatform platform)
    {
        var library = new TestMediaLibrary();
        library.Add("root", TestMediaLibrary.Folder("tracks", "Tracks"), TestMediaLibrary.Folder("lists", "Playlists"));
        library.Add("tracks", TestMediaLibrary.Track("t1", "First"));
        library.Add("lists", TestMediaLibrary.Track("t1", "First"), TestMediaLibrary.Track("t1", "First"));

        using var setup = await ConnectAsync(platform, library);

        var tabs = Assert.IsType<CarTabTemplate>(Assert.Single(setup.Session.Stack));
        Assert.Empty(setup.Host.Errors);
        var ids = tabs.Tabs.OfType<CarListTemplate>().SelectMany(t => t.Sections.SelectMany(s => s.Items)).Select(i => i.Id).ToArray();
        Assert.Equal(3, ids.Distinct(StringComparer.Ordinal).Count());
        await setup.Host.SelectAsync(((CarListTemplate)tabs.Tabs[1]).Sections.Single().Items[1]);
        Assert.Equal(["Play:t1"], setup.Player.Calls);
    }

    // Review I4: "More" in a tab's list updates the tab template (UpdateAsync only knows stack templates)
    [Theory]
    [MemberData(nameof(Platforms))]
    public async Task More_in_a_tab_list_appends_the_next_page(CarPlatform platform)
    {
        var library = new TestMediaLibrary();
        library.Add("root", TestMediaLibrary.Folder("albums", "Albums"), TestMediaLibrary.Folder("lists", "Playlists"));
        library.Add("albums", [.. Enumerable.Range(0, 25).Select(i => TestMediaLibrary.Track("a" + i, "Album " + i))]);
        library.Add("lists", TestMediaLibrary.Track("t1", "First"));

        using var setup = await ConnectAsync(platform, library, configure: host => host.SetLimit(CarLimitKind.ListItems, 41));
        var albums = (CarListTemplate)((CarTabTemplate)setup.Top).Tabs[0];
        await setup.Host.SelectAsync(Row(albums, CarMediaAutoBrowseHandler.MoreText));

        Assert.Empty(setup.Errors.Errors);
        Assert.Empty(setup.Host.Errors);
        var updated = (CarListTemplate)((CarTabTemplate)setup.Top).Tabs[0];
        Assert.Equal(albums.Id, updated.Id);
        Assert.Equal(25, updated.Sections.Single().Items.Count);
        Assert.Equal("First", Row(((CarTabTemplate)setup.Top).Tabs[1], "First").Text.Text);
    }

    // Review I5: a track at the depth limit (tabs + 4 levels) still shows Now Playing, and Up Next fits within the limit
    [Theory]
    [MemberData(nameof(Platforms))]
    public async Task Track_at_the_depth_limit_still_shows_now_playing_and_up_next(CarPlatform platform)
    {
        var library = new TestMediaLibrary();
        library.Add("root", TestMediaLibrary.Folder("genres", "Genres"), TestMediaLibrary.Folder("lists", "Playlists"));
        library.Add("genres", TestMediaLibrary.Folder("g1", "Jazz"));
        library.Add("g1", TestMediaLibrary.Folder("artist", "Performer"));
        library.Add("artist", TestMediaLibrary.Folder("album", "Album"));
        library.Add("album", TestMediaLibrary.Folder("cd", "CD 1"));
        library.Add("cd", TestMediaLibrary.Track("t9", "Deep"));
        library.Add("lists", TestMediaLibrary.Track("t1", "First"));

        using var setup = await ConnectAsync(platform, library, nowPlaying: CarNowPlayingButtons.Default([1f]) with { IsUpNextButtonEnabled = true });
        await setup.Host.SelectAsync(Row(((CarTabTemplate)setup.Top).Tabs[0], "Jazz"));
        foreach (var text in new[] { "Performer", "Album", "CD 1", "Deep" })
        {
            await setup.Host.SelectAsync(Row(setup.Top, text));
        }

        Assert.Equal(["Play:t9"], setup.Player.Calls);
        var nowPlaying = Assert.IsType<CarNowPlayingTemplate>(setup.Top);
        setup.Player.Emit(TestMediaFactory.Playing("t9") with { Queue = [TestMediaLibrary.Track("t9", "Deep")], QueueIndex = 0 });
        var session = (CarSession)setup.Session;
        await session.InvokeAsync(nowPlaying.OnUpNextSelected, session.CreateContext(nowPlaying), "UpNext");

        Assert.IsType<CarListTemplate>(setup.Top);
        Assert.Empty(setup.Host.Errors);
        Assert.Empty(setup.Errors.Errors);
    }

    // Review I8: ICarMediaLibrary is called on the main thread also after the library's asynchronous response
    [Theory]
    [MemberData(nameof(Platforms))]
    public async Task Library_calls_stay_on_the_main_thread(CarPlatform platform)
    {
        var library = Library();
        using var setup = await ConnectAsync(platform, library, configure: host => library.MainThreadCheck = () => !host.Dispatcher.IsDispatchRequired, yieldLibrary: true);
        await setup.Host.SelectAsync(Row(((CarTabTemplate)setup.Top).Tabs[0], "Album 1"));

        Assert.NotEmpty(library.ThreadChecks);
        Assert.All(library.ThreadChecks, onMain => Assert.True(onMain));
    }

    // Review M11: Android Auto without media templates draws playback itself: auto-browse does not send Now Playing
    [Fact]
    public async Task Android_auto_without_templated_media_does_not_push_now_playing()
    {
        using var setup = await ConnectAsync(CarPlatform.AndroidAuto, templatedMedia: false);
        await setup.Host.SelectAsync(Row(((CarTabTemplate)setup.Top).Tabs[2], "Third"));

        Assert.Equal(["Play:t3"], setup.Player.Calls);
        Assert.Single(setup.Session.Stack);
        Assert.Empty(setup.Host.Errors);
    }
}
