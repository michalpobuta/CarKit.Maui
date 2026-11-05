using CarKit.Maui;
using CarKit.Maui.Media;

namespace CarKit.Sample.Media;

// Sample library (spec, Stage 5 acceptance: ≥ 3 levels, search): Albums → tracks; Genres → artists → albums → tracks;
// Playlists → a list of 45 tones (paging), "Favorites" and "Live" (a broadcast with the sports mode, Stage 9). Every
// track plays one of the bundled tones.
public sealed class SampleMediaLibrary : ICarMediaLibrary
{
    public const string RootId = "root";

    // Duration of the generated WAV tones (Resources/Raw/tones)
    public static readonly TimeSpan ToneDuration = TimeSpan.FromSeconds(12);

    private static readonly string[] _tones = ["tones/tone_a.wav", "tones/tone_c.wav", "tones/tone_e.wav", "tones/tone_g.wav"];

    private readonly Dictionary<string, List<CarMediaItem>> _children = [];
    private readonly Dictionary<string, CarMediaItem> _items = [];
    private readonly Dictionary<string, string> _containers = [];
    private readonly Dictionary<string, string> _assets = [];

    public SampleMediaLibrary()
    {
        Root = new CarMediaItem(RootId, "CarKit Media") { IsBrowsable = true };
        var albums = new[]
        {
            Album("morning", "Morning", "Tone Quartet", "album_morning.png", ["Sunrise", "Coffee", "Road", "Sun"]),
            Album("night", "Night", "Tone Quartet", "album_night.png", ["Moon", "Silence", "Stars", "Dawn"]),
            Album("city", "City", "Chord Duet", "album_city.png", ["Tram", "Neon", "Bridge", "Square"]),
        };

        Add(RootId, Folder("albums", "Albums"), Folder("genres", "Genres"), Folder("playlists", "Playlists"));
        Add("albums", albums);

        // Genre → artist → album → tracks (four levels under the root)
        Add("genres", Folder("genre-ambient", "Ambient"), Folder("genre-jazz", "Jazz"));
        Add("genre-ambient", Folder("artist-quartet", "Tone Quartet"));
        Add("genre-jazz", Folder("artist-duet", "Chord Duet"));
        Add("artist-quartet", albums[0], albums[1]);
        Add("artist-duet", albums[2]);

        var longList = Folder("playlist-long", "45 tones");
        Add("playlists", longList, Folder("playlist-favorites", "Favorites"), Folder("playlist-live", "Live"));
        // Stage 9: a match broadcast. CarPlay (iOS 18.4) shows the sports mode while it plays; Android Auto shows the
        // regular Now Playing
        Add("playlist-live", Track("live-match", "Red Lions – Blue Hawks (live)", "Radio Sport", "playlist-live", 0) with
        {
            Sports = new CarNowPlayingSports(
                new CarSportsTeam("Red Lions") { Score = "2", Standings = "1.", IsFavorite = true },
                new CarSportsTeam("Blue Hawks") { Score = "1", Standings = "3." })
            {
                Status = new CarText(["2nd half", "2H"]),
                Clock = new CarSportsClock(TimeSpan.FromMinutes(67), countsUp: true),
            },
        });
        Add("playlist-long", [.. Enumerable.Range(1, 45).Select(i => Track("long-" + i.ToString("00", System.Globalization.CultureInfo.InvariantCulture), "Tone " + i, "Generator", "playlist-long", i))]);
        Add("playlist-favorites", [.. _children["morning"].Take(2).Concat(_children["city"].Take(2))]);
    }

    public CarMediaItem Root { get; }

    public CarMediaItem? Find(string id) => _items.GetValueOrDefault(id);

    // The queue of a track: the album or playlist it comes from
    public IReadOnlyList<CarMediaItem> QueueFor(string trackId) =>
        _containers.TryGetValue(trackId, out var container) ? _children[container] : Find(trackId) is { } track ? [track] : [];

    public string AssetFor(string trackId) => _assets.TryGetValue(trackId, out var asset) ? asset : _tones[0];

    public IReadOnlyList<CarMediaItem> ChildrenOf(string parentId) => _children.TryGetValue(parentId, out var items) ? items : [];

    public Task<CarMediaItem> GetRootAsync(CancellationToken ct) => Task.FromResult(Root);

    public Task<IReadOnlyList<CarMediaItem>> GetChildrenAsync(string parentId, int page, int pageSize, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<CarMediaItem>>([.. ChildrenOf(parentId).Skip(page * pageSize).Take(pageSize)]);

    public Task<IReadOnlyList<CarMediaItem>> SearchAsync(string query, CancellationToken ct)
    {
        var text = query.Trim();
        IReadOnlyList<CarMediaItem> results = text.Length == 0
            ? []
            : [.. _items.Values
                .Where(i => i.IsPlayable && (i.Title.Contains(text, StringComparison.CurrentCultureIgnoreCase) || (i.Subtitle?.Contains(text, StringComparison.CurrentCultureIgnoreCase) ?? false)))
                .OrderBy(i => i.Title, StringComparer.CurrentCulture)];
        return Task.FromResult(results);
    }

    public Task<CarMediaItem?> GetItemAsync(string id, CancellationToken ct) => Task.FromResult(Find(id));

    private static CarMediaItem Folder(string id, string title) => new(id, title) { IsBrowsable = true };

    private CarMediaItem Album(string id, string title, string artist, string cover, string[] tracks)
    {
        var album = new CarMediaItem(id, title)
        {
            Subtitle = artist,
            Artwork = CarImage.FromFile(cover),
            IsBrowsable = true,
            ChildrenStyle = CarMediaChildrenStyle.List,
        };
        Add(id, [.. tracks.Select((name, i) => Track(id + "-" + (i + 1), name, artist, id, i) with { Artwork = album.Artwork })]);
        return album;
    }

    private CarMediaItem Track(string id, string title, string artist, string container, int index)
    {
        _containers[id] = container;
        _assets[id] = _tones[index % _tones.Length];
        return new CarMediaItem(id, title) { Subtitle = artist, IsPlayable = true, Duration = ToneDuration };
    }

    private void Add(string parentId, params CarMediaItem[] items)
    {
        if (!_children.TryGetValue(parentId, out var list))
        {
            _children[parentId] = list = [];
        }

        list.AddRange(items);
        foreach (var item in items)
        {
            _items.TryAdd(item.Id, item);
        }
    }
}
