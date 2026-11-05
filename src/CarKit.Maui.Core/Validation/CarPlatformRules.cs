using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CarKit.Maui;

// Version requirement: iOS for CarPlay, Car App API level for Android; no value = satisfied
internal readonly record struct CarVersionRequirement(Version? MinIosVersion, int? MinApiLevel)
{
    public bool IsSatisfiedBy(ICarCapabilities capabilities) =>
        (MinIosVersion is null || capabilities.IosVersion is null || capabilities.IosVersion >= MinIosVersion)
        && (MinApiLevel is null || capabilities.CarAppApiLevel is null || capabilities.CarAppApiLevel >= MinApiLevel);
}

// Variants: template variant requirements (name from CarTemplateVariants.Of), e.g. QR code sign-in from Car App API 4
internal sealed record CarTemplateRule(CarVersionRequirement Requirement, int? MaxActions, IReadOnlyDictionary<string, CarVersionRequirement> Variants);

// Items: list row kinds allowed in a category (CarListItemKinds, e.g. conversations only in Communication; Stage 6)
internal sealed record CarCategoryRule(IReadOnlyDictionary<CarTemplateKind, CarVersionRequirement> Templates, int MaxDepth, bool Verified)
{
    public IReadOnlySet<string> Items { get; init; } = new HashSet<string>();

    // Stage 10: category alert action limit (CarPlay Audio: 1) when the host did not provide a runtime limit
    // (FakeCarHost, read error)
    public int? AlertActions { get; init; }
}

// List row kinds with their own rules ("items" in Rules/*.json); a row without a kind has no category restrictions
internal static class CarListItemKinds
{
    // CPMessageListItem (iOS 14), ConversationItem (Car App API 7)
    public const string Conversation = "Conversation";

    public static IReadOnlySet<string> Known { get; } = new HashSet<string>(StringComparer.Ordinal) { Conversation };

    public static string? Of(CarListTemplateItem item) => item is CarConversationItem ? Conversation : null;
}

// Platform rules from the embedded JSON (Rules/*.json); data, not code — an update does not change the renderers
internal sealed class CarPlatformRules
{
    private static readonly ConcurrentDictionary<string, CarPlatformRules> _cache = new();

    private CarPlatformRules(
        IReadOnlyDictionary<CarTemplateKind, CarTemplateRule> templates,
        IReadOnlyDictionary<CarAppCategory, CarCategoryRule> categories,
        IReadOnlySet<CarTemplateKind> tabChildren,
        IReadOnlySet<CarTemplateKind> rootOnly,
        IReadOnlyDictionary<CarLimitKind, int> defaultLimits,
        IReadOnlyDictionary<string, CarVersionRequirement> items,
        IReadOnlyDictionary<CarFeature, CarVersionRequirement> features)
    {
        Items = items;
        Features = features;
        Templates = templates;
        Categories = categories;
        TabChildren = tabChildren;
        RootOnly = rootOnly;
        DefaultLimits = defaultLimits;
    }

    public IReadOnlyDictionary<CarTemplateKind, CarTemplateRule> Templates { get; }

    // Version requirements of row kinds (CarListItemKinds); below them the platform renderer uses a fallback (Stage 6)
    public IReadOnlyDictionary<string, CarVersionRequirement> Items { get; }

    public IReadOnlyDictionary<CarAppCategory, CarCategoryRule> Categories { get; }

    // Stage 9: platform features with a version requirement ("features"); no entry = the feature is absent on the
    // platform
    public IReadOnlyDictionary<CarFeature, CarVersionRequirement> Features { get; }

    public IReadOnlySet<CarTemplateKind> TabChildren { get; }

    // Templates that the platform accepts only as a root (CarPlay: CPTabBarTemplate)
    public IReadOnlySet<CarTemplateKind> RootOnly { get; }

    public IReadOnlyDictionary<CarLimitKind, int> DefaultLimits { get; }

    public static CarPlatformRules For(CarPlatform platform)
    {
        var resource = platform == CarPlatform.CarPlay ? "carplay.json" : "androidauto.json";
        return _cache.GetOrAdd(resource, static name =>
        {
            using var stream = typeof(CarPlatformRules).Assembly.GetManifestResourceStream("CarKit.Maui.Rules." + name)
                ?? throw new InvalidDataException($"Missing embedded rules '{name}'.");
            using var reader = new StreamReader(stream);
            return Parse(reader.ReadToEnd());
        });
    }

    public static CarPlatformRules Parse(string json)
    {
        CarRulesDocument document;
        try
        {
            document = JsonSerializer.Deserialize(json, CarRulesJsonContext.Default.CarRulesDocument)
                ?? throw new InvalidDataException("Rules document is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Rules document is not valid JSON.", ex);
        }

        var templates = document.Templates.ToDictionary(
            pair => ParseName<CarTemplateKind>(pair.Key),
            pair => new CarTemplateRule(
                Requirement(pair.Value),
                pair.Value.MaxActions,
                (pair.Value.Variants ?? []).ToDictionary(variant => variant.Key, variant => Requirement(variant.Value))));
        var categories = document.Categories.ToDictionary(
            pair => ParseName<CarAppCategory>(pair.Key),
            pair => new CarCategoryRule(
                pair.Value.Templates.ToDictionary(t => ParseName<CarTemplateKind>(t.Key), t => Requirement(t.Value)),
                pair.Value.MaxDepth,
                pair.Value.Verified)
            {
                Items = (pair.Value.Items ?? []).Select(ParseItem).ToHashSet(StringComparer.Ordinal),
                AlertActions = pair.Value.AlertActions,
            });
        var tabChildren = document.TabChildren.Select(ParseName<CarTemplateKind>).ToHashSet();
        var rootOnly = document.RootOnly.Select(ParseName<CarTemplateKind>).ToHashSet();
        var limits = document.DefaultLimits.ToDictionary(pair => ParseName<CarLimitKind>(pair.Key), pair => pair.Value);
        var items = (document.Items ?? []).ToDictionary(pair => ParseItem(pair.Key), pair => Requirement(pair.Value), StringComparer.Ordinal);
        var features = (document.Features ?? []).ToDictionary(pair => ParseName<CarFeature>(pair.Key), pair => Requirement(pair.Value));
        return new CarPlatformRules(templates, categories, tabChildren, rootOnly, limits, items, features);
    }

    private static CarVersionRequirement Requirement(CarVersionDto? dto)
    {
        if (dto?.MinVersion is { } text && !Version.TryParse(text, out _))
        {
            throw new InvalidDataException($"Invalid version '{text}' in rules.");
        }

        return new(dto?.MinVersion is { } version ? Version.Parse(version) : null, dto?.MinApiLevel);
    }

    private static string ParseItem(string name) =>
        CarListItemKinds.Known.Contains(name) ? name : throw new InvalidDataException($"Unknown list item kind '{name}' in rules.");

    private static TEnum ParseName<TEnum>(string name) where TEnum : struct, Enum =>
        Enum.TryParse<TEnum>(name, ignoreCase: false, out var value) && Enum.IsDefined(value)
            ? value
            : throw new InvalidDataException($"Unknown {typeof(TEnum).Name} '{name}' in rules.");
}

internal class CarVersionDto
{
    public string? MinVersion { get; set; }

    public int? MinApiLevel { get; set; }
}

internal sealed class CarTemplateRuleDto : CarVersionDto
{
    public int? MaxActions { get; set; }

    public Dictionary<string, CarVersionDto?>? Variants { get; set; }
}

internal sealed class CarCategoryRuleDto
{
    public Dictionary<string, CarVersionDto?> Templates { get; set; } = [];

    public int MaxDepth { get; set; }

    public bool Verified { get; set; } = true;

    public List<string>? Items { get; set; }

    public int? AlertActions { get; set; }
}

internal sealed class CarRulesDocument
{
    public string? Source { get; set; }

    public Dictionary<string, CarTemplateRuleDto> Templates { get; set; } = [];

    public Dictionary<string, CarCategoryRuleDto> Categories { get; set; } = [];

    public List<string> TabChildren { get; set; } = [];

    public List<string> RootOnly { get; set; } = [];

    public Dictionary<string, int> DefaultLimits { get; set; } = [];

    public Dictionary<string, CarVersionDto?>? Items { get; set; }

    public Dictionary<string, CarVersionDto?>? Features { get; set; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, ReadCommentHandling = JsonCommentHandling.Skip)]
[JsonSerializable(typeof(CarRulesDocument))]
internal sealed partial class CarRulesJsonContext : JsonSerializerContext;
