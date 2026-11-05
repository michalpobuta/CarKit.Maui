using CarKit.Maui.Core.Tests.Validation;
using Xunit;

namespace CarKit.Maui.Core.Tests.Navigation;

// Stage 8: CarMapTemplate data without which the host rejects the template
// (spec: "Navigation module decisions (Stage 8)", Validation)
public class NavigationValidationTests
{
    private static readonly CarImage _icon = CarImage.FromSystemSymbol("plus.magnifyingglass", "ic_zoom_in");

    private static CarMapTemplate Map(int buttons = 0, bool pan = false) => new()
    {
        Id = "map",
        IsPanningEnabled = pan,
        MapButtons = [.. Enumerable.Range(0, buttons).Select(i => new CarMapButton(_icon) { Id = "b" + i })],
        TrailingBarButtons = [new CarBarButton("Destination") { Id = "dest" }],
    };

    [Fact]
    public void Map_on_android_auto_needs_a_trailing_button()
    {
        var bare = Map() with { TrailingBarButtons = [] };

        var ex = Assert.Throws<CarKitException>(() => V.Create(CarAppCategory.Navigation, CarPlatform.AndroidAuto).Prepare(bare, 1));
        Assert.Contains("TrailingBarButtons", ex.Message, StringComparison.Ordinal);
        Assert.Same(bare, V.Create(CarAppCategory.Navigation).Prepare(bare, 1));
    }

    [Theory]
    [InlineData(CarPlatform.CarPlay, false, 4)]
    [InlineData(CarPlatform.AndroidAuto, false, 4)]
    [InlineData(CarPlatform.AndroidAuto, true, 3)]
    [InlineData(CarPlatform.CarPlay, true, 4)]
    public void Map_buttons_over_the_limit(CarPlatform platform, bool pan, int expected)
    {
        var validator = V.Create(CarAppCategory.Navigation, platform);

        var map = Assert.IsType<CarMapTemplate>(validator.Prepare(Map(6, pan), 1));
        Assert.Equal(expected, map.MapButtons.Count);
        Assert.Equal("b0", map.MapButtons[0].Id);

        var strict = V.Create(CarAppCategory.Navigation, platform, configure: o => o.Limits.OnOverflow = CarOverflowBehavior.Throw);
        var ex = Assert.Throws<CarTemplateLimitException>(() => strict.Prepare(Map(6, pan), 1));
        Assert.Equal(CarLimitKind.MapButtons, ex.Limit);
        Assert.Equal(expected, ex.Maximum);
    }

    [Fact]
    public void Map_template_follows_the_category_matrix()
    {
        Assert.Throws<CarTemplateNotAllowedException>(() => V.Create(CarAppCategory.Parking).Prepare(Map(), 1));
        Assert.Throws<CarTemplateNotAllowedException>(() => V.Create(CarAppCategory.Parking, CarPlatform.AndroidAuto).Prepare(Map(), 1));
        Assert.NotNull(V.Create(CarAppCategory.Navigation | CarAppCategory.Parking).Prepare(Map(2), 1));
    }
}
