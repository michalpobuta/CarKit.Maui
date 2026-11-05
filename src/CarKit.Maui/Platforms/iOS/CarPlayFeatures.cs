using CarPlay;

namespace CarKit.Maui;

// Stage 9: features rendered by this CarKit asset (spec: "New platform features decisions (Stage 9)", Feature gate).
// net10.0-ios (ref package 26.0): only sports mode (iOS 18.4); net10.0-ios26.4: also iOS 26.4 features. The OS version
// is checked by the rules ("features"), and video by the car session configuration
// (CPSessionConfiguration.SupportsVideoPlayback).
internal sealed class CarPlayFeatures : IDisposable
{
    // After the disconnect the host reports no features (the car session configuration is released)
    private bool _closed;
#if IOS26_4_OR_GREATER
    private readonly CPSessionConfiguration? _configuration;
    private readonly ConfigurationDelegate? _delegate;
#endif

    public CarPlayFeatures()
    {
#if IOS26_4_OR_GREATER
        if (OperatingSystem.IsIOSVersionAtLeast(26, 4))
        {
            // The delegate is weak on the Objective-C side; we keep it with the configuration for the duration of the
            // connection
            _delegate = new ConfigurationDelegate();
            _configuration = new CPSessionConfiguration(_delegate);
        }
#endif
    }

    // Features compiled into this asset (without dependence on the car)
    public static bool IsCompiled(CarFeature feature) => feature switch
    {
        CarFeature.NowPlayingSportsMode => true,
#if IOS26_4_OR_GREATER
        CarFeature.VoiceControlActionButtons or CarFeature.ListDetailsHeader or CarFeature.PlaybackConfiguration
            or CarFeature.RouteSharing or CarFeature.VideoPlayback => true,
#endif
        _ => false,
    };

    public bool Supports(CarFeature feature) => !_closed && IsCompiled(feature) && (feature != CarFeature.VideoPlayback || SupportsVideo());

    public void Dispose()
    {
        _closed = true;
#if IOS26_4_OR_GREATER
        _configuration?.Dispose();
        _delegate?.Dispose();
#endif
    }

#if IOS26_4_OR_GREATER
    private bool SupportsVideo() => _configuration is { } configuration && OperatingSystem.IsIOSVersionAtLeast(26, 4) && configuration.SupportsVideoPlayback;
#else
    // The 26.0 asset does not know CPSessionConfiguration.SupportsVideoPlayback (iOS 26.4); IsCompiled returns false anyway
    private static bool SupportsVideo() => false;
#endif

#if IOS26_4_OR_GREATER
    private sealed class ConfigurationDelegate : CPSessionConfigurationDelegate;
#endif
}
