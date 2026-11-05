using CarKit.Maui.Renderers;
using Xunit;

namespace CarKit.Maui.Tests.Renderers;

public class RefreshRulesTests
{
    private static CarAppTemplateShape List(string title, params string[] rows) =>
        new(CarAppTemplateTypes.List, title, IsLoading: false) { Sections = [Section(null, rows)] };

    private static CarAppSectionShape Section(string? title, params string[] rows) =>
        new(title, [.. rows.Select(r => new CarAppRowShape(r))]);

    [Fact]
    public void Previous_loading_is_always_a_refresh()
    {
        var loading = new CarAppTemplateShape(CarAppTemplateTypes.List, "Start", IsLoading: true);

        Assert.True(CarAppRefreshRules.IsRefresh(loading, List("Other title", "a", "b")));
        Assert.True(CarAppRefreshRules.IsRefresh(loading with { Type = CarAppTemplateTypes.Grid }, List("x") with { Type = CarAppTemplateTypes.Grid }));
    }

    // Review Focus 4: the detail text alone does not change the shape (row titles are the same)
    [Fact]
    public void Detail_text_change_is_a_refresh() =>
        Assert.True(CarAppRefreshRules.IsRefresh(List("Parking lots", "A", "B"), List("Parking lots", "A", "B")));

    [Fact]
    public void Row_count_change_is_not_a_refresh() =>
        Assert.False(CarAppRefreshRules.IsRefresh(List("Parking lots", "A", "B"), List("Parking lots", "A", "B", "C")));

    [Fact]
    public void Row_title_change_is_not_a_refresh() =>
        Assert.False(CarAppRefreshRules.IsRefresh(List("Parking lots", "A", "B"), List("Parking lots", "A", "X")));

    [Fact]
    public void Row_title_change_with_toggle_change_is_a_refresh()
    {
        var off = List("Settings") with { Sections = [new CarAppSectionShape(null, [new CarAppRowShape("Wi-Fi: off", Toggle: false)])] };
        var on = List("Settings") with { Sections = [new CarAppSectionShape(null, [new CarAppRowShape("Wi-Fi: on", Toggle: true)])] };
        var renamed = List("Settings") with { Sections = [new CarAppSectionShape(null, [new CarAppRowShape("Wi-Fi", Toggle: false)])] };

        Assert.True(CarAppRefreshRules.IsRefresh(off, on));
        Assert.False(CarAppRefreshRules.IsRefresh(off, renamed));
    }

    [Fact]
    public void Section_header_change_is_not_a_refresh()
    {
        var previous = List("T") with { Sections = [Section("A", "1"), Section("B", "2")] };

        Assert.False(CarAppRefreshRules.IsRefresh(previous, previous with { Sections = [Section("A", "1"), Section("C", "2")] }));
        Assert.False(CarAppRefreshRules.IsRefresh(previous, previous with { Sections = [Section("A", "1")] }));
        Assert.True(CarAppRefreshRules.IsRefresh(previous, previous with { Sections = [Section("A", "1"), Section("B", "2")] }));
    }

    [Fact]
    public void Title_change_is_not_a_refresh() =>
        Assert.False(CarAppRefreshRules.IsRefresh(List("A", "1"), List("B", "1")));

    [Fact]
    public void Different_type_is_not_a_refresh() =>
        Assert.False(CarAppRefreshRules.IsRefresh(List("A", "1"), List("A", "1") with { Type = CarAppTemplateTypes.Sectioned }));

    [Fact]
    public void Grid_and_pane_follow_the_list_rule()
    {
        var grid = List("G", "a", "b") with { Type = CarAppTemplateTypes.Grid };
        var pane = List("P", "Price") with { Type = CarAppTemplateTypes.Pane };

        Assert.True(CarAppRefreshRules.IsRefresh(grid, grid with { }));
        Assert.False(CarAppRefreshRules.IsRefresh(grid, List("G", "a") with { Type = CarAppTemplateTypes.Grid }));
        Assert.False(CarAppRefreshRules.IsRefresh(pane, List("P", "Address") with { Type = CarAppTemplateTypes.Pane }));
    }

    [Fact]
    public void Search_changes_are_refreshes()
    {
        var search = new CarAppTemplateShape(CarAppTemplateTypes.Search, null, IsLoading: false);

        Assert.True(CarAppRefreshRules.IsRefresh(search, search with { Sections = [Section(null, "Result")] }));
    }

    [Fact]
    public void Message_text_change_is_not_a_refresh()
    {
        var message = new CarAppTemplateShape(CarAppTemplateTypes.Message, "Attention", IsLoading: false) { Message = "Gate open" };

        Assert.True(CarAppRefreshRules.IsRefresh(message, message with { }));
        Assert.False(CarAppRefreshRules.IsRefresh(message, message with { Message = "Gate closed" }));
        Assert.False(CarAppRefreshRules.IsRefresh(message with { Type = CarAppTemplateTypes.LongMessage }, message with { Type = CarAppTemplateTypes.LongMessage, Title = "Other" }));
    }

    // Kind of sign-in method in the shape's Message; loading → always a refresh
    [Fact]
    public void Sign_in_refresh_needs_the_same_title_and_method()
    {
        var pin = new CarAppTemplateShape(CarAppTemplateTypes.SignIn, "Sign-in", IsLoading: false) { Message = "Pin" };

        Assert.True(CarAppRefreshRules.IsRefresh(pin, pin with { }));
        Assert.False(CarAppRefreshRules.IsRefresh(pin, pin with { Message = "Input" }));
        Assert.False(CarAppRefreshRules.IsRefresh(pin, pin with { Title = "Other" }));
        Assert.True(CarAppRefreshRules.IsRefresh(pin with { IsLoading = true }, pin with { Message = "Input" }));
    }

    private static CarAppTemplateShape Tabs(string active, CarAppTemplateShape content) =>
        new(CarAppTemplateTypes.Tab, null, IsLoading: false)
        {
            Tabs = [new CarAppTabShape("List", "res:1"), new CarAppTabShape("Grid", "res:2")],
            ActiveTab = active,
            ActiveContent = content,
        };

    [Fact]
    public void Tab_switch_is_a_refresh()
    {
        Assert.True(CarAppRefreshRules.IsRefresh(Tabs("a", List("L", "1")), Tabs("b", List("G", "x", "y") with { Type = CarAppTemplateTypes.Grid })));
        Assert.True(CarAppRefreshRules.IsRefresh(Tabs("a", List("L", "1")), Tabs("a", List("L", "1"))));
        var renamed = Tabs("a", List("L", "1")) with { Tabs = [new CarAppTabShape("Other", "res:1"), new CarAppTabShape("Grid", "res:2")] };
        Assert.False(CarAppRefreshRules.IsRefresh(Tabs("a", List("L", "1")), renamed));
    }

    [Fact]
    public void Active_tab_content_change_is_not_a_refresh() =>
        Assert.False(CarAppRefreshRules.IsRefresh(Tabs("a", List("L", "1")), Tabs("a", List("L", "1", "2"))));
}
