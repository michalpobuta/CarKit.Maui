namespace CarKit.Maui.Media;

// Icon of the Now Playing button outside CarPlay (Media3 CommandButton.ICON_*); CarPlay has its own CPNowPlaying*Button
// types
internal enum CarMediaButtonIcon
{
    ShuffleOn,
    ShuffleOff,
    RepeatOff,
    RepeatOne,
    RepeatAll,
    PlaybackRate,
    AddToLibrary,
    More,
    Custom,
}

// A Now Playing button with state applied: Shuffle/Repeat selection from the player state (or IsSelected),
// the Media3 icon and the Media3 session command identifier (spec: "Media module decisions (Stage 5)")
internal sealed record CarResolvedNowPlayingButton(CarNowPlayingButton Button, bool IsSelected, bool IsEnabled, CarMediaButtonIcon Icon, string CommandId);

internal static class CarNowPlayingButtons
{
    public const string ShuffleCommand = "carkit.shuffle";
    public const string RepeatCommand = "carkit.repeat";
    public const string RateCommand = "carkit.rate";
    public const string CustomCommandPrefix = "carkit.custom:";

    // Default Now Playing: Shuffle and Repeat, a rate button only when there is more than one rate
    public static CarNowPlayingTemplate Default(IReadOnlyList<float> rates)
    {
        CarNowPlayingButton[] buttons = rates.Count > 1
            ? [Button(CarNowPlayingButtonKind.Shuffle), Button(CarNowPlayingButtonKind.Repeat), Button(CarNowPlayingButtonKind.PlaybackRate)]
            : [Button(CarNowPlayingButtonKind.Shuffle), Button(CarNowPlayingButtonKind.Repeat)];
        return new CarNowPlayingTemplate { Id = "carkit-now-playing", Buttons = [.. buttons] };
    }

    public static IReadOnlyList<CarResolvedNowPlayingButton> Resolve(CarNowPlayingTemplate template, CarPlaybackState state) =>
        [.. template.Buttons.Select(button => Resolve(button, state))];

    public static CarResolvedNowPlayingButton Resolve(CarNowPlayingButton button, CarPlaybackState state) => button.Kind switch
    {
        CarNowPlayingButtonKind.Shuffle => new(
            button, button.IsSelected || state.IsShuffleEnabled, button.IsEnabled,
            state.IsShuffleEnabled ? CarMediaButtonIcon.ShuffleOn : CarMediaButtonIcon.ShuffleOff, ShuffleCommand),
        CarNowPlayingButtonKind.Repeat => new(
            button, button.IsSelected || state.RepeatMode != CarRepeatMode.Off, button.IsEnabled,
            state.RepeatMode switch
            {
                CarRepeatMode.One => CarMediaButtonIcon.RepeatOne,
                CarRepeatMode.All => CarMediaButtonIcon.RepeatAll,
                _ => CarMediaButtonIcon.RepeatOff,
            },
            RepeatCommand),
        CarNowPlayingButtonKind.PlaybackRate => new(button, button.IsSelected, button.IsEnabled, CarMediaButtonIcon.PlaybackRate, RateCommand),
        // Legacy Media3 actions must have an icon (otherwise IllegalArgumentException when Android Auto connects);
        // Media3 icons
        CarNowPlayingButtonKind.AddToLibrary => new(button, button.IsSelected, button.IsEnabled, CarMediaButtonIcon.AddToLibrary, CustomCommandPrefix + button.Id),
        CarNowPlayingButtonKind.More => new(button, button.IsSelected, button.IsEnabled, CarMediaButtonIcon.More, CustomCommandPrefix + button.Id),
        _ => new(button, button.IsSelected, button.IsEnabled, CarMediaButtonIcon.Custom, CustomCommandPrefix + button.Id),
    };

    // Template button for a session command (Media3 OnCustomCommand)
    public static CarNowPlayingButton? Find(CarNowPlayingTemplate template, string commandId) =>
        template.Buttons.FirstOrDefault(b => Resolve(b, new CarPlaybackState()).CommandId == commandId);

    private static CarNowPlayingButton Button(CarNowPlayingButtonKind kind) => new(kind) { Id = "carkit-" + kind.ToString().ToLowerInvariant() };
}
