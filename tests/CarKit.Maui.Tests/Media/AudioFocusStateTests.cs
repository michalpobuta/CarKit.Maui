using CarKit.Maui.Media;
using Xunit;

namespace CarKit.Maui.Tests.Media;

// Android audio focus state (CarAudioFocus) without AudioManager
public class AudioFocusStateTests
{
    private static readonly CarPlaybackState _playing = TestMediaFactory.Playing();
    private static readonly CarPlaybackState _paused = TestMediaFactory.Playing() with { Status = CarPlaybackStatus.Paused };

    [Fact]
    public void Playing_requests_focus_once_and_stopping_abandons_it()
    {
        var focus = new CarAudioFocusState();

        Assert.Equal(CarAudioFocusAction.Request, focus.OnPlaybackState(_playing, resumePending: false));
        focus.OnRequestResult(granted: true);
        Assert.True(focus.IsHeld);
        Assert.Equal(CarAudioFocusAction.None, focus.OnPlaybackState(_playing, resumePending: false));
        Assert.Equal(CarAudioFocusAction.None, focus.OnPlaybackState(_paused, resumePending: false));
        Assert.Equal(CarAudioFocusAction.Abandon, focus.OnPlaybackState(_playing with { Status = CarPlaybackStatus.Ended }, resumePending: false));
        Assert.False(focus.IsHeld);
        Assert.Equal(CarAudioFocusAction.None, focus.OnPlaybackState(new CarPlaybackState(), resumePending: false));
    }

    // Review I2: a denial (e.g. during a call) must not be remembered as focus: the next playback asks again
    [Fact]
    public void Denied_request_is_retried_on_the_next_play()
    {
        var focus = new CarAudioFocusState();
        Assert.Equal(CarAudioFocusAction.Request, focus.OnPlaybackState(_playing, resumePending: false));
        focus.OnRequestResult(granted: false);
        Assert.False(focus.IsHeld);
        Assert.Equal(CarAudioFocusAction.None, focus.OnPlaybackState(_paused, resumePending: false));

        Assert.Equal(CarAudioFocusAction.Request, focus.OnPlaybackState(_playing, resumePending: false));
    }

    [Fact]
    public void Transient_loss_waits_for_gain_unless_the_driver_plays_again()
    {
        var focus = new CarAudioFocusState();
        focus.OnPlaybackState(_playing, resumePending: false);
        focus.OnRequestResult(granted: true);

        focus.OnTransientLoss();
        Assert.False(focus.IsHeld);
        Assert.Equal(CarAudioFocusAction.None, focus.OnPlaybackState(_playing, resumePending: true));
        Assert.Equal(CarAudioFocusAction.Request, focus.OnPlaybackState(_playing, resumePending: false));
        focus.OnGain();
        Assert.True(focus.IsHeld);

        focus.OnLoss();
        Assert.False(focus.IsHeld);
        Assert.Equal(CarAudioFocusAction.Request, focus.OnPlaybackState(_playing, resumePending: false));
    }
}
