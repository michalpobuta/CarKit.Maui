using CarKit.Maui.Media;
using Xunit;

namespace CarKit.Maui.Tests.Media;

// Interruption policy shared by iOS (AVAudioSession) and Android (audio focus)
public class InterruptionTests
{
    [Fact]
    public async Task Interruption_pauses_and_resumes_when_allowed()
    {
        var (controller, player, _, _) = TestMediaFactory.Controller();
        player.Emit(TestMediaFactory.Playing());

        await controller.Interruptions.BeganAsync();
        player.Emit(controller.State with { Status = CarPlaybackStatus.Paused });
        await controller.Interruptions.EndedAsync(shouldResume: true);

        Assert.Equal(["Pause", "Resume"], player.Calls);
    }

    [Fact]
    public async Task Interruption_without_resume_option_stays_paused()
    {
        var (controller, player, _, _) = TestMediaFactory.Controller();
        player.Emit(TestMediaFactory.Playing());

        await controller.Interruptions.BeganAsync();
        await controller.Interruptions.EndedAsync(shouldResume: false);
        await controller.Interruptions.EndedAsync(shouldResume: true);

        Assert.Equal(["Pause"], player.Calls);
    }

    // Review Focus 3: the driver paused during a call: the end of the interruption does not resume
    [Fact]
    public async Task User_pause_during_an_interruption_cancels_the_resume()
    {
        var (controller, player, _, _) = TestMediaFactory.Controller();
        player.Emit(TestMediaFactory.Playing());

        await controller.Interruptions.BeganAsync();
        await controller.PauseAsync();
        await controller.Interruptions.EndedAsync(shouldResume: true);

        Assert.Equal(["Pause", "Pause"], player.Calls);
    }

    [Fact]
    public async Task Permanent_focus_loss_and_disconnected_output_do_not_resume()
    {
        var (controller, player, _, _) = TestMediaFactory.Controller();
        player.Emit(TestMediaFactory.Playing());

        await controller.Interruptions.FocusLostAsync(transient: true);
        await controller.Interruptions.FocusLostAsync(transient: false);
        await controller.Interruptions.FocusGainedAsync();
        player.Emit(TestMediaFactory.Playing());
        await controller.Interruptions.OutputDisconnectedAsync();
        await controller.Interruptions.EndedAsync(shouldResume: true);

        Assert.Equal(["Pause", "Pause", "Pause"], player.Calls);
    }

    [Fact]
    public async Task Interruption_while_paused_does_not_resume()
    {
        var (controller, player, time, _) = TestMediaFactory.Controller();
        player.Emit(TestMediaFactory.Playing() with { Status = CarPlaybackStatus.Paused });
        time.Advance(TimeSpan.FromSeconds(5));

        await controller.Interruptions.BeganAsync();
        await controller.Interruptions.EndedAsync(shouldResume: true);

        Assert.Empty(player.Calls);
    }

    // iOS stops AVAudioPlayer by itself, and the app may report the pause before the interruption notification
    [Fact]
    public async Task Interruption_right_after_the_player_paused_itself_still_resumes()
    {
        var (controller, player, time, _) = TestMediaFactory.Controller();
        player.Emit(TestMediaFactory.Playing());
        player.Emit(controller.State with { Status = CarPlaybackStatus.Paused });
        time.Advance(TimeSpan.FromMilliseconds(300));

        await controller.Interruptions.BeganAsync();
        await controller.Interruptions.EndedAsync(shouldResume: true);

        Assert.Equal(["Pause", "Resume"], player.Calls);
    }

    [Fact]
    public async Task Transient_focus_loss_resumes_on_gain()
    {
        var (controller, player, _, _) = TestMediaFactory.Controller();
        player.Emit(TestMediaFactory.Playing() with { Status = CarPlaybackStatus.Buffering });

        await controller.Interruptions.FocusLostAsync(transient: true);
        await controller.Interruptions.FocusGainedAsync();

        Assert.Equal(["Pause", "Resume"], player.Calls);
    }
}
