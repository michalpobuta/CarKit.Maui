using Android.Media;

namespace CarKit.Sample.Media;

// Android: MediaPlayer from a tone file in the APK assets (MauiAsset, .wav uncompressed); rate through PlaybackParams.
// Audio focus, BECOMING_NOISY and the Media3 session are handled by CarKit.Maui.Media; the sample only plays the file.
public sealed class SampleAudioOutput : ISampleAudioOutput, IDisposable
{
    private MediaPlayer? _player;
    private float _rate = 1f;

    public event EventHandler? Completed;

    public TimeSpan Position => _player is { } player ? TimeSpan.FromMilliseconds(player.CurrentPosition) : TimeSpan.Zero;

    public bool IsPlaying => _player?.IsPlaying ?? false;

    public void Load(string asset)
    {
        Dispose();
        using var file = Android.App.Application.Context.Assets!.OpenFd(asset);
        var player = new MediaPlayer();
        player.SetAudioAttributes(new AudioAttributes.Builder().SetUsage(AudioUsageKind.Media)!.SetContentType(AudioContentType.Music)!.Build()!);
        player.SetDataSource(file.FileDescriptor, file.StartOffset, file.Length);
        player.Prepare();
        player.Completion += OnCompletion;
        _player = player;
    }

    public void Play()
    {
        if (_player is { } player)
        {
            player.Start();
            ApplyRate(player);
        }
    }

    public void Pause()
    {
        if (_player is { IsPlaying: true } player)
        {
            player.Pause();
        }
    }

    public void Seek(TimeSpan position) => _player?.SeekTo((int)Math.Max(0, position.TotalMilliseconds));

    public void SetRate(float rate)
    {
        _rate = rate;
        if (_player is { IsPlaying: true } player)
        {
            ApplyRate(player);
        }
    }

    public void Dispose()
    {
        if (_player is { } player)
        {
            player.Completion -= OnCompletion;
            player.Release();
            player.Dispose();
            _player = null;
        }
    }

    // setPlaybackParams on a stopped MediaPlayer starts it, so the rate is applied only during playback
    private void ApplyRate(MediaPlayer player) => player.PlaybackParams = new PlaybackParams().SetSpeed(_rate)!;

    // The next track releases this MediaPlayer, so only after its callback returns
    // (MainThread.BeginInvokeOnMainThread runs immediately on the main thread, hence Handler.Post)
    private void OnCompletion(object? sender, EventArgs e) => new Android.OS.Handler(Android.OS.Looper.MainLooper!).Post(() => Completed?.Invoke(this, EventArgs.Empty));
}
