namespace CarKit.Maui.Media;

// Playback position at the moment of reading: the state carries the position from the moment of emission, and while
// Playing it keeps advancing at the playback rate. Media3 (SimpleBasePlayer) and MPNowPlayingInfo extrapolate from the
// value they receive, so it has to be supplied "as of now".
internal static class CarPlaybackClock
{
    public static TimeSpan Position(CarPlaybackState state, TimeSpan sinceReceived)
    {
        if (state.Status != CarPlaybackStatus.Playing || sinceReceived <= TimeSpan.Zero)
        {
            return state.Position;
        }

        var position = state.Position + (sinceReceived * state.Rate);
        return state.EffectiveDuration is { } duration && duration > TimeSpan.Zero && position > duration ? duration : position;
    }
}
