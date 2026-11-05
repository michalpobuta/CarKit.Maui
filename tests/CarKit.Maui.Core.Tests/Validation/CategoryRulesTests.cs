using CarKit.Maui.Core.Tests.Model;
using Xunit;

namespace CarKit.Maui.Core.Tests.Validation;

// Stage 7: full rows of the CarPlay matrix from the spec ("Allowed CarPlay template matrix"), a template outside the category
// and the depth of each category on both platforms (data from Rules/*.json)
public class CategoryRulesTests
{
    private static readonly CarTemplateKind[] _matrixColumns =
    [
        CarTemplateKind.List, CarTemplateKind.Grid, CarTemplateKind.Tab, CarTemplateKind.Information, CarTemplateKind.PointOfInterest,
        CarTemplateKind.Search, CarTemplateKind.Map, CarTemplateKind.NowPlaying, CarTemplateKind.CarPlayContact, CarTemplateKind.Alert,
        CarTemplateKind.ActionSheet,
    ];

    // Table rows from the spec: ✓ = 1, – = 0, in the order of _matrixColumns (Alert / Action sheet are two columns)
    private static readonly Dictionary<CarAppCategory, string> _matrix = new()
    {
        [CarAppCategory.Audio] = "11100001011",
        [CarAppCategory.Communication] = "11110000111",
        [CarAppCategory.Navigation] = "11110110011",
        [CarAppCategory.Parking] = "11111000011",
        [CarAppCategory.EvCharging] = "11111000011",
        [CarAppCategory.Fueling] = "11111000011",
        [CarAppCategory.QuickFoodOrdering] = "11111000011",
        [CarAppCategory.DrivingTask] = "11111000011",
    };

    public static TheoryData<CarAppCategory> MatrixRows() => [.. _matrix.Keys];

    [Theory]
    [MemberData(nameof(MatrixRows))]
    public void CarPlay_category_matrix_matches_the_spec(CarAppCategory category)
    {
        var validator = V.Create(category);
        for (var column = 0; column < _matrixColumns.Length; column++)
        {
            var kind = _matrixColumns[column];
            var template = V.Of(kind);
            var allowed = _matrix[category][column] == '1';
            var depth = template is CarModalTemplate ? (int?)null : 1;
            if (allowed)
            {
                Assert.NotNull(validator.Prepare(template, depth));
            }
            else
            {
                var ex = Assert.Throws<CarTemplateNotAllowedException>(() => validator.Prepare(template, depth));
                Assert.Equal(kind, ex.Kind);
            }
        }
    }

    [Theory]
    [InlineData(CarPlatform.CarPlay, CarAppCategory.Audio, CarTemplateKind.Information)]
    [InlineData(CarPlatform.CarPlay, CarAppCategory.Communication, CarTemplateKind.PointOfInterest)]
    [InlineData(CarPlatform.CarPlay, CarAppCategory.Navigation, CarTemplateKind.PointOfInterest)]
    [InlineData(CarPlatform.CarPlay, CarAppCategory.Parking, CarTemplateKind.Search)]
    [InlineData(CarPlatform.CarPlay, CarAppCategory.EvCharging, CarTemplateKind.Map)]
    [InlineData(CarPlatform.CarPlay, CarAppCategory.Fueling, CarTemplateKind.NowPlaying)]
    [InlineData(CarPlatform.CarPlay, CarAppCategory.QuickFoodOrdering, CarTemplateKind.CarPlayContact)]
    [InlineData(CarPlatform.CarPlay, CarAppCategory.DrivingTask, CarTemplateKind.Search)]
    [InlineData(CarPlatform.CarPlay, CarAppCategory.VoiceConversational, CarTemplateKind.PointOfInterest)]
    [InlineData(CarPlatform.CarPlay, CarAppCategory.Video, CarTemplateKind.PointOfInterest)]
    [InlineData(CarPlatform.CarPlay, CarAppCategory.PublicSafety, CarTemplateKind.Search)]
    [InlineData(CarPlatform.CarPlay, CarAppCategory.Iot, CarTemplateKind.Grid)]
    [InlineData(CarPlatform.CarPlay, CarAppCategory.Weather, CarTemplateKind.Information)]
    [InlineData(CarPlatform.AndroidAuto, CarAppCategory.Audio, CarTemplateKind.PointOfInterest)]
    [InlineData(CarPlatform.AndroidAuto, CarAppCategory.Communication, CarTemplateKind.PointOfInterest)]
    [InlineData(CarPlatform.AndroidAuto, CarAppCategory.Navigation, CarTemplateKind.NowPlaying)]
    [InlineData(CarPlatform.AndroidAuto, CarAppCategory.Parking, CarTemplateKind.Map)]
    [InlineData(CarPlatform.AndroidAuto, CarAppCategory.EvCharging, CarTemplateKind.Map)]
    [InlineData(CarPlatform.AndroidAuto, CarAppCategory.Fueling, CarTemplateKind.NowPlaying)]
    [InlineData(CarPlatform.AndroidAuto, CarAppCategory.Iot, CarTemplateKind.PointOfInterest)]
    [InlineData(CarPlatform.AndroidAuto, CarAppCategory.Weather, CarTemplateKind.Search)]
    [InlineData(CarPlatform.AndroidAuto, CarAppCategory.QuickFoodOrdering, CarTemplateKind.List)]
    [InlineData(CarPlatform.AndroidAuto, CarAppCategory.DrivingTask, CarTemplateKind.Grid)]
    [InlineData(CarPlatform.AndroidAuto, CarAppCategory.VoiceConversational, CarTemplateKind.Message)]
    [InlineData(CarPlatform.AndroidAuto, CarAppCategory.PublicSafety, CarTemplateKind.List)]
    public void Template_outside_the_category_is_rejected(CarPlatform platform, CarAppCategory category, CarTemplateKind kind)
    {
        var template = Valid(V.Of(kind));

        var ex = Assert.Throws<CarTemplateNotAllowedException>(() => V.Create(category, platform).Prepare(template, template is CarModalTemplate ? null : 1));

        Assert.Equal(kind, ex.Kind);
        Assert.Equal(category, ex.Categories);
    }

    [Theory]
    [InlineData(CarPlatform.CarPlay, CarAppCategory.Audio, 5)]
    [InlineData(CarPlatform.CarPlay, CarAppCategory.Communication, 5)]
    [InlineData(CarPlatform.CarPlay, CarAppCategory.Navigation, 5)]
    [InlineData(CarPlatform.CarPlay, CarAppCategory.Parking, 5)]
    [InlineData(CarPlatform.CarPlay, CarAppCategory.EvCharging, 5)]
    [InlineData(CarPlatform.CarPlay, CarAppCategory.Fueling, 3)]
    [InlineData(CarPlatform.CarPlay, CarAppCategory.QuickFoodOrdering, 3)]
    [InlineData(CarPlatform.CarPlay, CarAppCategory.DrivingTask, 3)]
    [InlineData(CarPlatform.CarPlay, CarAppCategory.VoiceConversational, 3)]
    [InlineData(CarPlatform.CarPlay, CarAppCategory.PublicSafety, 5)]
    [InlineData(CarPlatform.AndroidAuto, CarAppCategory.Parking, 5)]
    [InlineData(CarPlatform.AndroidAuto, CarAppCategory.EvCharging, 5)]
    [InlineData(CarPlatform.AndroidAuto, CarAppCategory.Fueling, 5)]
    [InlineData(CarPlatform.AndroidAuto, CarAppCategory.Iot, 5)]
    [InlineData(CarPlatform.AndroidAuto, CarAppCategory.Weather, 5)]
    public void Stack_over_the_category_depth_is_rejected(CarPlatform platform, CarAppCategory category, int maxDepth)
    {
        var validator = V.Create(category, platform);
        var list = new CarListTemplate("List");

        Assert.Same(list, validator.Prepare(list, maxDepth));
        var ex = Assert.Throws<CarTemplateLimitException>(() => validator.Prepare(list, maxDepth + 1));
        Assert.Equal(CarLimitKind.StackDepth, ex.Limit);
        Assert.Equal(maxDepth, ex.Maximum);
    }

    [Theory]
    [InlineData(CarAppCategory.VoiceConversational, "26.4", true)]
    [InlineData(CarAppCategory.VoiceConversational, "26.0", false)]
    [InlineData(CarAppCategory.Navigation, "26.0", true)]
    [InlineData(CarAppCategory.DrivingTask, "26.4", false)]
    public void Voice_control_is_for_conversational_apps_from_ios_26_4(CarAppCategory category, string ios, bool allowed)
    {
        var validator = V.Create(category, ios: Version.Parse(ios));
        var voice = V.Of(CarTemplateKind.VoiceControl);

        if (allowed)
        {
            Assert.Same(voice, validator.Prepare(voice, null));
        }
        else
        {
            Assert.Throws<CarTemplateNotAllowedException>(() => validator.Prepare(voice, null));
        }
    }

    // A POI template with one point that has a distance (data required on Android Auto), the others unchanged
    private static CarTemplate Valid(CarTemplate template) =>
        template is CarPointOfInterestTemplate poi ? poi with { Points = [P.Point("a")] } : template;
}
