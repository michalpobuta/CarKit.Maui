namespace CarKit.Maui;

// Stage 8: the modules' shared AVAudioSession state (spec: "Navigation module decisions (Stage 8)", Voice prompts).
// The Media module reports that its session is playing (Playback active), and then the Navigation module's voice
// prompts neither change the category nor deactivate the session (the app's playback would go quiet). Any thread.
internal static class CarAudioSessionCoordinator
{
    private static int _mediaActive;

    public static bool MediaActive
    {
        get => Volatile.Read(ref _mediaActive) == 1;
        set => Volatile.Write(ref _mediaActive, value ? 1 : 0);
    }
}
