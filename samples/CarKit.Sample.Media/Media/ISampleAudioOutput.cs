namespace CarKit.Sample.Media;

// Platform audio output (AVAudioPlayer on iOS, MediaPlayer on Android); tests on net10.0 supply a fake output
public interface ISampleAudioOutput
{
    TimeSpan Position { get; }

    bool IsPlaying { get; }

    event EventHandler? Completed;

    // A tone file from MauiAsset (e.g. tones/tone_a.wav)
    void Load(string asset);

    void Play();

    void Pause();

    void Seek(TimeSpan position);

    void SetRate(float rate);
}
