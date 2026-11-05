using AndroidX.Media3.Session;

namespace CarKit.Maui.Media;

// Process media session for Android Auto media templates (RegisterMediaPlaybackToken): the service reports opening
// and closing, the MediaPlaybackTemplate renderer waits for the session if the car connected earlier. Main thread only.
internal static class CarMediaSessions
{
    public static MediaSession? Current { get; private set; }

    public static event Action<MediaSession>? SessionOpened;

    public static void Opened(MediaSession session)
    {
        Current = session;
        SessionOpened?.Invoke(session);
    }

    public static void Closed(MediaSession session)
    {
        if (ReferenceEquals(Current, session))
        {
            Current = null;
        }
    }
}
