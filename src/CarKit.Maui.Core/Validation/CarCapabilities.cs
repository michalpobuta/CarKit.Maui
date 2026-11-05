namespace CarKit.Maui;

// ICarCapabilities implementation for hosts: platform rules + limits read from the host at runtime
internal sealed class CarCapabilities : ICarCapabilities
{
    private readonly Func<CarLimitKind, int?>? _runtimeLimits;
    private readonly Func<CarFeature, bool> _assetFeatures;

    // assetFeatures (Stage 9): features rendered by the host asset (iOS: asset 26.0/26.4, car video); null = the latest
    // asset
    public CarCapabilities(
        CarPlatform platform, Version? iosVersion, int? carAppApiLevel, Func<CarLimitKind, int?>? runtimeLimits = null, Func<CarFeature, bool>? assetFeatures = null)
    {
        Platform = platform;
        IosVersion = iosVersion;
        CarAppApiLevel = carAppApiLevel;
        Rules = CarPlatformRules.For(platform);
        _runtimeLimits = runtimeLimits;
        _assetFeatures = assetFeatures ?? CarFeatureDefaults.NewestAsset.Contains;
    }

    public CarPlatform Platform { get; }

    public Version? IosVersion { get; }

    public int? CarAppApiLevel { get; }

    public CarPlatformRules Rules { get; }

    public bool Supports(CarTemplateKind kind) =>
        Rules.Templates.TryGetValue(kind, out var rule) && rule.Requirement.IsSatisfiedBy(this);

    // Stage 9: a requirement from the "features" section of the rules (no entry = absent on the platform) and the host
    // asset
    public bool Supports(CarFeature feature) =>
        Rules.Features.TryGetValue(feature, out var requirement) && requirement.IsSatisfiedBy(this) && _assetFeatures(feature);

    public int? GetLimit(CarLimitKind kind) =>
        _runtimeLimits?.Invoke(kind) ?? (Rules.DefaultLimits.TryGetValue(kind, out var limit) ? limit : null);
}
