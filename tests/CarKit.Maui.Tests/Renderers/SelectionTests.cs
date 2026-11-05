using CarKit.Maui.Renderers;
using Xunit;

namespace CarKit.Maui.Tests.Renderers;

public class TabSelectionTests
{
    private static CarTabTemplate Tabs(string? selected) => new([new CarListTemplate("A") { Id = "a" }, new CarGridTemplate("B") { Id = "b" }])
    {
        SelectedTabId = selected,
    };

    [Fact]
    public void User_selection_wins_until_model_changes_selection()
    {
        Assert.Equal("b", CarTabSelection.Active(Tabs("a"), Tabs("a"), userSelected: "b"));
        Assert.Equal("a", CarTabSelection.Active(Tabs(null), Tabs("a"), userSelected: "b"));
        Assert.Equal("b", CarTabSelection.Active(null, Tabs(null), userSelected: "b"));
        Assert.Equal("a", CarTabSelection.Active(null, Tabs("a"), userSelected: null));
    }

    [Fact]
    public void Missing_tab_falls_back_to_first()
    {
        Assert.Equal("a", CarTabSelection.Active(null, Tabs("x"), userSelected: "y"));
        Assert.Null(CarTabSelection.Active(null, new CarTabTemplate([]), userSelected: null));
    }
}

public class SearchRequestsTests
{
    [Fact]
    public void Only_the_latest_request_applies()
    {
        var requests = new CarSearchRequests();

        var first = requests.Begin("par");
        var second = requests.Begin("park");

        Assert.False(requests.IsLatest(first));
        Assert.True(requests.IsLatest(second));
        Assert.Equal("park", requests.Text);
    }
}

public class RuntimeLimitsTests
{
    [Fact]
    public void Runtime_limits_are_read_once()
    {
        var reads = 0;
        var limits = new CarRuntimeLimits(kind =>
        {
            reads++;
            return kind == CarLimitKind.ListItems ? 24 : null;
        }, new TestLogger());

        Assert.Equal(24, limits.Get(CarLimitKind.ListItems));
        Assert.Equal(24, limits.Get(CarLimitKind.ListItems));
        Assert.Null(limits.Get(CarLimitKind.Tabs));
        Assert.Null(limits.Get(CarLimitKind.Tabs));
        Assert.Equal(2, reads);
    }

    [Fact]
    public void Failing_or_zero_limit_falls_back_to_rules()
    {
        var logger = new TestLogger();
        var limits = new CarRuntimeLimits(kind => kind == CarLimitKind.GridItems ? throw new InvalidOperationException("host") : 0, logger);
        var capabilities = new CarCapabilities(CarPlatform.CarPlay, new Version(26, 4), null, limits.Get);

        Assert.Equal(8, capabilities.GetLimit(CarLimitKind.GridItems));
        Assert.Equal(12, capabilities.GetLimit(CarLimitKind.ListItems));
        Assert.Single(logger.Entries, e => e.Level == Microsoft.Extensions.Logging.LogLevel.Warning);
    }

    [Fact]
    public void Sectioned_template_needs_api_8_and_the_switch()
    {
        Assert.True(CarRenderSwitches.UseSectionedItemTemplate(8, switchValue: null));
        Assert.True(CarRenderSwitches.UseSectionedItemTemplate(9, switchValue: true));
        Assert.False(CarRenderSwitches.UseSectionedItemTemplate(7, switchValue: true));
        Assert.False(CarRenderSwitches.UseSectionedItemTemplate(8, switchValue: false));
        Assert.Equal("CarKit.Maui.Android.SectionedItemTemplate", CarRenderSwitches.SectionedItemTemplate);
    }

    // Stage 6: RowSection accepts only Row, so a conversation list (ConversationItem) is always a ListTemplate
    [Fact]
    public void Communication_lists_never_use_sectioned_template()
    {
        Assert.False(CarRenderSwitches.UseSectionedItemTemplate(8, switchValue: null, CarAppCategory.Communication));
        Assert.False(CarRenderSwitches.UseSectionedItemTemplate(8, switchValue: true, CarAppCategory.Parking | CarAppCategory.Communication));
        Assert.True(CarRenderSwitches.UseSectionedItemTemplate(8, switchValue: null, CarAppCategory.Parking));
    }
}
