using CarKit.Maui.Media;
using Xunit;

namespace CarKit.Maui.Tests.Media;

public class NowPlayingButtonsTests
{
    [Fact]
    public void Shuffle_and_repeat_selection_follows_the_state()
    {
        var template = CarNowPlayingButtons.Default([1f]);

        var off = CarNowPlayingButtons.Resolve(template, new CarPlaybackState());
        Assert.Equal([false, false], off.Select(b => b.IsSelected));
        Assert.Equal([CarMediaButtonIcon.ShuffleOff, CarMediaButtonIcon.RepeatOff], off.Select(b => b.Icon));

        var on = CarNowPlayingButtons.Resolve(template, new CarPlaybackState { IsShuffleEnabled = true, RepeatMode = CarRepeatMode.One });
        Assert.Equal([true, true], on.Select(b => b.IsSelected));
        Assert.Equal([CarMediaButtonIcon.ShuffleOn, CarMediaButtonIcon.RepeatOne], on.Select(b => b.Icon));
        Assert.Equal([CarNowPlayingButtons.ShuffleCommand, CarNowPlayingButtons.RepeatCommand], on.Select(b => b.CommandId));
        Assert.Equal(CarMediaButtonIcon.RepeatAll, CarNowPlayingButtons.Resolve(template, new CarPlaybackState { RepeatMode = CarRepeatMode.All })[1].Icon);
    }

    [Fact]
    public void Default_buttons_add_rate_only_with_several_rates()
    {
        Assert.Equal(2, CarNowPlayingButtons.Default([1f]).Buttons.Count);
        var withRate = CarNowPlayingButtons.Default([1f, 1.5f]);
        Assert.Equal(CarNowPlayingButtonKind.PlaybackRate, withRate.Buttons[2].Kind);
        Assert.Equal(CarNowPlayingButtons.RateCommand, CarNowPlayingButtons.Resolve(withRate, new CarPlaybackState())[2].CommandId);
    }

    [Fact]
    public void Custom_buttons_invoke_the_custom_action()
    {
        var like = new CarNowPlayingButton(CarNowPlayingButtonKind.Custom) { Id = "like", IsSelected = true, IsEnabled = false };
        var template = new CarNowPlayingTemplate { Buttons = [like, new CarNowPlayingButton(CarNowPlayingButtonKind.More) { Id = "more" }] };

        var resolved = CarNowPlayingButtons.Resolve(template, new CarPlaybackState());

        Assert.Equal("carkit.custom:like", resolved[0].CommandId);
        Assert.True(resolved[0].IsSelected);
        Assert.False(resolved[0].IsEnabled);
        Assert.Same(like, CarNowPlayingButtons.Find(template, "carkit.custom:like"));
        Assert.Equal("more", CarNowPlayingButtons.Find(template, "carkit.custom:more")?.Id);
        Assert.Null(CarNowPlayingButtons.Find(template, "carkit.custom:other"));
    }

    // Review C1: Media3 throws IllegalArgumentException for a legacy action without an icon: AddToLibrary and More get
    // Media3 icons
    [Fact]
    public void Add_to_library_and_more_have_media3_icons()
    {
        var template = new CarNowPlayingTemplate
        {
            Buttons = [new CarNowPlayingButton(CarNowPlayingButtonKind.AddToLibrary) { Id = "add" }, new CarNowPlayingButton(CarNowPlayingButtonKind.More) { Id = "more" }],
        };

        var resolved = CarNowPlayingButtons.Resolve(template, new CarPlaybackState());

        Assert.Equal([CarMediaButtonIcon.AddToLibrary, CarMediaButtonIcon.More], resolved.Select(b => b.Icon));
        Assert.Equal(["carkit.custom:add", "carkit.custom:more"], resolved.Select(b => b.CommandId));
    }
}
