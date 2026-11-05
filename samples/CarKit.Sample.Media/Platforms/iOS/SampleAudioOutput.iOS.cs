using AVFoundation;
using Foundation;

namespace CarKit.Sample.Media;

// iOS: AVAudioPlayer from a tone file in the bundle (MauiAsset); rate through EnableRate. The audio session (Playback,
// interruptions) is handled by CarKit.Maui.Media; the sample only plays the file.
public sealed class SampleAudioOutput : ISampleAudioOutput, IDisposable
{
    private AVAudioPlayer? _player;
    private float _rate = 1f;

    public event EventHandler? Completed;

    public TimeSpan Position => _player is { } player ? TimeSpan.FromSeconds(player.CurrentTime) : TimeSpan.Zero;

    public bool IsPlaying => _player?.Playing ?? false;

    public void Load(string asset)
    {
        Dispose();
        var url = NSUrl.FromFilename(Path.Combine(NSBundle.MainBundle.BundlePath, asset));
        _player = AVAudioPlayer.FromUrl(url, out var error)
            ?? throw new InvalidOperationException($"Cannot open {asset}: {error?.LocalizedDescription}");
        _player.EnableRate = true;
        _player.Rate = _rate;
        _player.FinishedPlaying += OnFinished;
        _player.PrepareToPlay();
    }

    public void Play() => _player?.Play();

    public void Pause() => _player?.Pause();

    public void Seek(TimeSpan position)
    {
        if (_player is { } player)
        {
            player.CurrentTime = Math.Clamp(position.TotalSeconds, 0, player.Duration);
        }
    }

    public void SetRate(float rate)
    {
        _rate = rate;
        if (_player is { } player)
        {
            player.Rate = rate;
        }
    }

    public void Dispose()
    {
        if (_player is { } player)
        {
            player.FinishedPlaying -= OnFinished;
            player.Stop();
            player.Dispose();
            _player = null;
        }
    }

    // The next track releases this AVAudioPlayer, so not in its callback (ObjectDisposedException → app crash)
    // (MainThread.BeginInvokeOnMainThread runs immediately on the main thread, hence the GCD queue)
    private void OnFinished(object? sender, AVStatusEventArgs e) => CoreFoundation.DispatchQueue.MainQueue.DispatchAsync(() => Completed?.Invoke(this, EventArgs.Empty));
}
