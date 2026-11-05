using CarKit.Maui;

namespace CarKit.Sample.Media;

// Sample Now Playing: shuffle, repeat, rate and "Like" (CarPlay: an image button, Android Auto: a session button),
// Up Next with the player queue (auto-browse)
public static class SampleNowPlaying
{
    public static CarNowPlayingTemplate Template { get; } = new()
    {
        Id = "sample-now-playing",
        Buttons =
        [
            new CarNowPlayingButton(CarNowPlayingButtonKind.Shuffle) { Id = "shuffle" },
            new CarNowPlayingButton(CarNowPlayingButtonKind.Repeat) { Id = "repeat" },
            new CarNowPlayingButton(CarNowPlayingButtonKind.PlaybackRate) { Id = "rate" },
            new CarNowPlayingButton(CarNowPlayingButtonKind.Custom) { Id = "like", Image = CarImage.FromSystemSymbol("heart", "ic_sample_like") },
        ],
        IsUpNextButtonEnabled = true,
        UpNextTitle = "Up Next",
    };
}
