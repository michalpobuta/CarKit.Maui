using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CarKit.Maui.Media;

/// <summary>Configures the media module inside <see cref="CarKitMediaBuilderExtensions.UseMedia(CarKitBuilder, Action{CarMediaBuilder})"/>.</summary>
public sealed class CarMediaBuilder
{
    internal CarMediaBuilder(IServiceCollection services, bool templatedMediaFromBuild)
    {
        Services = services;
        TemplatedMediaFromBuild = templatedMediaFromBuild;
        UseTemplatedMediaOnAndroid = templatedMediaFromBuild;
    }

    /// <summary>Gets the app services.</summary>
    public IServiceCollection Services { get; }

    /// <summary>Gets the playback rates the car offers (CarPlay remote command, rate button); defaults to 1 only.</summary>
    public IList<float> PlaybackRates { get; } = [1f];

    /// <summary>
    /// Gets or sets the Now Playing screen: pushed by auto-browse and the source of the Android Auto session buttons.
    /// Defaults to shuffle and repeat buttons, plus a rate button when <see cref="PlaybackRates"/> has more than one rate.
    /// </summary>
    public CarNowPlayingTemplate? NowPlaying { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether Android Auto shows Now Playing with the Car App Library
    /// <c>MediaPlaybackTemplate</c> (Car App API 8, Early Access). Defaults to <c>&lt;CarKitAndroidTemplatedMedia&gt;</c>
    /// from the app project file, which also generates the manifest entries; a different value fails at startup.
    /// </summary>
    public bool UseTemplatedMediaOnAndroid { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether CarKit requests Android audio focus for the app player and pauses and resumes
    /// it when another app (a phone call, navigation) takes the focus. Defaults to <see langword="true"/>. Set it to
    /// <see langword="false"/> when the player handles audio focus itself (for example ExoPlayer with
    /// <c>handleAudioFocus</c>); two focus requests from one app take the focus from each other. iOS is not affected.
    /// </summary>
    public bool HandleAudioFocus { get; set; } = true;

    internal bool TemplatedMediaFromBuild { get; }

    internal Action<IServiceCollection>? LibraryRegistration { get; private set; }

    internal Action<IServiceCollection>? PlayerRegistration { get; private set; }

    internal bool AutoBrowse { get; private set; }

    /// <summary>
    /// Registers the app's media library as a singleton; <see cref="ICarMediaLibrary"/> resolves to the same instance as
    /// <typeparamref name="TLibrary"/>. A later call replaces an earlier one.
    /// </summary>
    /// <typeparam name="TLibrary">The library type, created by dependency injection.</typeparam>
    /// <returns>This builder.</returns>
    public CarMediaBuilder UseLibrary<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TLibrary>()
        where TLibrary : class, ICarMediaLibrary
    {
        LibraryRegistration = static services =>
        {
            services.TryAddSingleton<TLibrary>();
            services.AddSingleton<ICarMediaLibrary>(static sp => sp.GetRequiredService<TLibrary>());
        };
        return this;
    }

    /// <summary>
    /// Registers the adapter over the app's player as a singleton; <see cref="ICarMediaPlayer"/> resolves to the same
    /// instance as <typeparamref name="TPlayer"/>, so the phone UI and the car share one player. A later call replaces an earlier one.
    /// </summary>
    /// <typeparam name="TPlayer">The player type, created by dependency injection.</typeparam>
    /// <returns>This builder.</returns>
    public CarMediaBuilder UsePlayer<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TPlayer>()
        where TPlayer : class, ICarMediaPlayer
    {
        PlayerRegistration = static services =>
        {
            services.TryAddSingleton<TPlayer>();
            services.AddSingleton<ICarMediaPlayer>(static sp => sp.GetRequiredService<TPlayer>());
        };
        return this;
    }

    /// <summary>
    /// Lets CarKit build the car screens from the library: tabs of lists on CarPlay (and on Android Auto with
    /// templated media), Now Playing after the driver picks a track. Replaces <c>car.UseHandler&lt;THandler&gt;()</c>.
    /// </summary>
    /// <returns>This builder.</returns>
    public CarMediaBuilder UseAutoBrowse()
    {
        AutoBrowse = true;
        return this;
    }

    internal CarMediaOptions Build()
    {
        if (LibraryRegistration is null || PlayerRegistration is null)
        {
            throw new InvalidOperationException("car.UseMedia needs both media.UseLibrary<TLibrary>() and media.UsePlayer<TPlayer>().");
        }

        if (UseTemplatedMediaOnAndroid != TemplatedMediaFromBuild)
        {
            throw new InvalidOperationException(
                $"media.UseTemplatedMediaOnAndroid ({UseTemplatedMediaOnAndroid}) differs from <CarKitAndroidTemplatedMedia> in the app project file ({TemplatedMediaFromBuild}). "
                + "The project file drives the generated manifest entries: set it there and leave media.UseTemplatedMediaOnAndroid unchanged.");
        }

        var rates = PlaybackRates.Distinct().ToArray();
        if (rates.Length == 0 || rates.Any(r => r <= 0 || !float.IsFinite(r)))
        {
            throw new InvalidOperationException("media.PlaybackRates needs at least one positive rate.");
        }

        return new CarMediaOptions(rates, NowPlaying ?? CarNowPlayingButtons.Default(rates), AutoBrowse, UseTemplatedMediaOnAndroid) { HandleAudioFocus = HandleAudioFocus };
    }
}

// Module configuration after UseMedia (immutable)
internal sealed record CarMediaOptions(IReadOnlyList<float> PlaybackRates, CarNowPlayingTemplate NowPlaying, bool AutoBrowse, bool TemplatedMedia)
{
    public bool HandleAudioFocus { get; init; } = true;
}
