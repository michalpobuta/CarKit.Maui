using CarKit.Maui.Media;
using CarKit.Maui.Tests.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CarKit.Maui.Tests.Media;

// UseMedia and relaxing the handler requirement (spec: "Media module decisions (Stage 5)")
public class MediaBuilderTests
{
    private static ServiceProvider Register(Action<CarKitBuilder> configure, CarAppCategory categories = CarAppCategory.Audio)
    {
        var services = new ServiceCollection();
        // MauiCarDispatcher requires a platform; tests on net10.0 supply their own (app services before UseCarKit win)
        services.AddSingleton<ICarDispatcher>(new InlineDispatcher());
        CarKitAppBuilderExtensions.Register(services, configure, categories);
        return services.BuildServiceProvider();
    }

    private static CarKitBuilder Media(CarKitBuilder car, Action<CarMediaBuilder> configure, bool templatedFromBuild = false) =>
        CarKitMediaBuilderExtensions.UseMedia(car, configure, templatedFromBuild);

    // Review Focus 5: an app with only a media library works without UseHandler
    [Fact]
    public void Media_only_app_needs_no_handler()
    {
        using var provider = Register(car => Media(car, m => m.UseLibrary<TestMediaLibrary>().UsePlayer<TestMediaPlayer>().UseAutoBrowse()));

        Assert.IsType<CarMediaAutoBrowseHandler>(provider.GetRequiredService<ICarAppHandler>());
        // The app type and the interface are the same instance (the phone screen and the car share one player)
        Assert.Same(provider.GetRequiredService<TestMediaPlayer>(), provider.GetRequiredService<ICarMediaPlayer>());
        Assert.Same(provider.GetRequiredService<TestMediaLibrary>(), provider.GetRequiredService<ICarMediaLibrary>());
        var runtime = provider.GetRequiredService<CarMediaRuntime>();
        Assert.IsType<TestMediaLibrary>(runtime.Library);
        Assert.Equal([1f], runtime.Options.PlaybackRates);
        Assert.Equal([CarNowPlayingButtonKind.Shuffle, CarNowPlayingButtonKind.Repeat], runtime.Options.NowPlaying.Buttons.Select(b => b.Kind));
    }

    [Fact]
    public void Media_without_auto_browse_needs_a_handler()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Register(car => Media(car, m => m.UseLibrary<TestMediaLibrary>().UsePlayer<TestMediaPlayer>())));
        Assert.Contains("UseAutoBrowse", error.Message, StringComparison.Ordinal);

        using var provider = Register(car => Media(car.UseHandler<RecordingHandler>(), m => m.UseLibrary<TestMediaLibrary>().UsePlayer<TestMediaPlayer>()));
        Assert.IsType<RecordingHandler>(provider.GetRequiredService<ICarAppHandler>());
    }

    [Fact]
    public void Auto_browse_and_handler_conflict()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            Register(car => Media(car.UseHandler<RecordingHandler>(), m => m.UseLibrary<TestMediaLibrary>().UsePlayer<TestMediaPlayer>().UseAutoBrowse())));
        Assert.Contains("media.UseAutoBrowse()", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Library_and_player_are_required()
    {
        Assert.Contains("UsePlayer", Assert.Throws<InvalidOperationException>(() =>
            Register(car => Media(car, m => m.UseLibrary<TestMediaLibrary>().UseAutoBrowse()))).Message, StringComparison.Ordinal);
        Assert.Contains("UseLibrary", Assert.Throws<InvalidOperationException>(() =>
            Register(car => Media(car, m => m.UsePlayer<TestMediaPlayer>().UseAutoBrowse()))).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Use_media_twice_fails()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Register(car =>
        {
            Media(car, m => m.UseLibrary<TestMediaLibrary>().UsePlayer<TestMediaPlayer>().UseAutoBrowse());
            Media(car, m => m.UseLibrary<TestMediaLibrary>().UsePlayer<TestMediaPlayer>().UseAutoBrowse());
        }));
        Assert.Contains("UseMedia", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Templated_media_flag_must_match_the_build()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Register(car => Media(car, m =>
        {
            m.UseLibrary<TestMediaLibrary>().UsePlayer<TestMediaPlayer>().UseAutoBrowse();
            m.UseTemplatedMediaOnAndroid = true;
        })));
        Assert.Contains("CarKitAndroidTemplatedMedia", error.Message, StringComparison.Ordinal);

        using var provider = Register(car => Media(car, m => m.UseLibrary<TestMediaLibrary>().UsePlayer<TestMediaPlayer>().UseAutoBrowse(), templatedFromBuild: true));
        Assert.True(provider.GetRequiredService<CarMediaOptions>().TemplatedMedia);
    }

    [Fact]
    public void Rates_and_now_playing_are_forwarded()
    {
        var custom = new CarNowPlayingTemplate { Buttons = [new CarNowPlayingButton(CarNowPlayingButtonKind.More)] };
        using var provider = Register(car => Media(car, m =>
        {
            m.UseLibrary<TestMediaLibrary>().UsePlayer<TestMediaPlayer>().UseAutoBrowse();
            m.PlaybackRates.Add(1.5f);
            m.PlaybackRates.Add(1.5f);
        }));
        var options = provider.GetRequiredService<CarMediaOptions>();
        Assert.Equal([1f, 1.5f], options.PlaybackRates);
        Assert.Equal(
            [CarNowPlayingButtonKind.Shuffle, CarNowPlayingButtonKind.Repeat, CarNowPlayingButtonKind.PlaybackRate],
            options.NowPlaying.Buttons.Select(b => b.Kind));

        using var withTemplate = Register(car => Media(car, m =>
        {
            m.UseLibrary<TestMediaLibrary>().UsePlayer<TestMediaPlayer>().UseAutoBrowse();
            m.NowPlaying = custom;
        }));
        Assert.Same(custom, withTemplate.GetRequiredService<CarMediaOptions>().NowPlaying);
        Assert.True(withTemplate.GetRequiredService<CarMediaOptions>().HandleAudioFocus);

        // Review I3: a player with its own audio focus (ExoPlayer handleAudioFocus) turns off the CarKit focus on Android
        using var ownFocus = Register(car => Media(car, m =>
        {
            m.UseLibrary<TestMediaLibrary>().UsePlayer<TestMediaPlayer>().UseAutoBrowse();
            m.HandleAudioFocus = false;
        }));
        Assert.False(ownFocus.GetRequiredService<CarMediaOptions>().HandleAudioFocus);

        Assert.Throws<InvalidOperationException>(() => Register(car => Media(car, m =>
        {
            m.UseLibrary<TestMediaLibrary>().UsePlayer<TestMediaPlayer>().UseAutoBrowse();
            m.PlaybackRates.Clear();
        })));
    }
}
