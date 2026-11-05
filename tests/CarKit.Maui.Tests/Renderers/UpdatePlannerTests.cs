using CarKit.Maui.Renderers;
using Xunit;

namespace CarKit.Maui.Tests.Renderers;

public class UpdatePlannerTests
{
    private static CarListTemplate List(string title, params CarListTemplateItem[] items) => new(title)
    {
        Id = "list",
        Sections = [new CarSection(items) { Id = "s", Header = "Section" }],
    };

    // CPListTemplate.Title is read-only
    [Fact]
    public void List_title_change_needs_replace() =>
        Assert.Equal(CarPlayUpdate.Replace, CarPlayUpdatePlanner.Plan(List("A"), List("B")));

    [Fact]
    public void List_content_change_is_in_place()
    {
        var previous = List("A", new CarListItem("1") { Id = "1" });
        var updated = previous with { Sections = [], EmptyViewTitle = "Empty", TrailingBarButtons = [new CarBarButton("X")] };

        Assert.Equal(CarPlayUpdate.InPlace, CarPlayUpdatePlanner.Plan(previous, updated));
    }

    // Review Focus 1: an update during a spinner must not replace CPListItem
    [Fact]
    public void Same_structure_updates_items_in_place()
    {
        var row = new CarListItem("Parking") { Id = "row" };
        var image = new CarListImageRowItem("Photos", [CarImage.FromFile("a.png")]) { Id = "images" };
        var previous = List("A", row, image);
        var updated = List("A",
            row with { Text = "Parking 2", Detail = "Free: 3", Image = CarImage.FromFile("p.png"), IsPlaying = true, PlaybackProgress = 0.5, IsEnabled = false },
            image with { Text = "Other photos", Images = [CarImage.FromFile("b.png"), CarImage.FromFile("c.png")] });

        Assert.True(CarPlayUpdatePlanner.HasSameItems(previous, updated));
        Assert.Equal(CarPlayUpdate.InPlace, CarPlayUpdatePlanner.Plan(previous, updated));
    }

    [Fact]
    public void New_row_or_reordered_rows_need_new_sections()
    {
        var a = new CarListItem("A") { Id = "a" };
        var b = new CarListItem("B") { Id = "b" };
        var previous = List("T", a, b);

        Assert.False(CarPlayUpdatePlanner.HasSameItems(previous, List("T", a, b, new CarListItem("C") { Id = "c" })));
        Assert.False(CarPlayUpdatePlanner.HasSameItems(previous, List("T", b, a)));
        Assert.False(CarPlayUpdatePlanner.HasSameItems(previous, previous with { Sections = [previous.Sections[0] with { Header = "Other" }] }));
        Assert.False(CarPlayUpdatePlanner.HasSameItems(previous, previous with { Sections = [previous.Sections[0] with { IndexTitle = "X" }] }));
        Assert.False(CarPlayUpdatePlanner.HasSameItems(previous, List("T", a, new CarListImageRowItem("B", []) { Id = "b" })));
        Assert.False(CarPlayUpdatePlanner.HasSameItems(previous, previous with { IsLoading = true }));
        Assert.True(CarPlayUpdatePlanner.HasSameItems(previous with { IsLoading = true }, previous with { IsLoading = true, EmptyViewTitle = "x" }));
    }

    [Fact]
    public void Information_layout_change_needs_replace()
    {
        var info = new CarInformationTemplate("Parking") { Items = [new CarInformationItem("Price", "$6")] };

        Assert.Equal(CarPlayUpdate.Replace, CarPlayUpdatePlanner.Plan(info, info with { Layout = CarInformationLayout.TwoColumn }));
        Assert.Equal(CarPlayUpdate.InPlace, CarPlayUpdatePlanner.Plan(info, info with { Title = "Other", Items = [], Actions = [new CarAction("OK")] }));
    }

    [Fact]
    public void Alert_with_same_texts_is_in_place()
    {
        var action = new CarAction("OK") { Id = "ok" };
        var alert = new CarAlertTemplate(new CarText(["Long title", "Short"])) { Actions = [action] };

        Assert.Equal(CarPlayUpdate.InPlace, CarPlayUpdatePlanner.Plan(alert, alert with { Actions = [action with { OnSelected = _ => Task.CompletedTask }], Subtitle = "AA" }));
    }

    [Fact]
    public void Alert_text_change_needs_replace()
    {
        var alert = new CarAlertTemplate("A") { Actions = [new CarAction("OK")] };
        var sheet = new CarActionSheetTemplate("A", "B") { Actions = [new CarAction("OK")] };

        Assert.Equal(CarPlayUpdate.Replace, CarPlayUpdatePlanner.Plan(alert, alert with { Title = "B" }));
        Assert.Equal(CarPlayUpdate.Replace, CarPlayUpdatePlanner.Plan(alert, alert with { Actions = [new CarAction("Cancel")] }));
        Assert.Equal(CarPlayUpdate.Replace, CarPlayUpdatePlanner.Plan(alert, alert with { Actions = [alert.Actions[0] with { Style = CarActionStyle.Destructive }] }));
        Assert.Equal(CarPlayUpdate.Replace, CarPlayUpdatePlanner.Plan(alert, alert with { Actions = [alert.Actions[0] with { IsEnabled = false }] }));
        Assert.Equal(CarPlayUpdate.Replace, CarPlayUpdatePlanner.Plan(sheet, sheet with { Message = "C" }));
        Assert.Equal(CarPlayUpdate.InPlace, CarPlayUpdatePlanner.Plan(sheet, sheet with { Actions = [sheet.Actions[0] with { OnSelected = _ => Task.CompletedTask }] }));
    }

    [Fact]
    public void Grid_tab_search_message_update_in_place()
    {
        var grid = new CarGridTemplate("G");
        var tabs = new CarTabTemplate([new CarListTemplate("A")]);
        var search = new CarSearchTemplate();
        var message = new CarMessageTemplate("M");

        Assert.Equal(CarPlayUpdate.InPlace, CarPlayUpdatePlanner.Plan(grid, grid with { Title = "H", Items = [new CarGridItem("X", CarImage.FromFile("x.png"))] }));
        Assert.Equal(CarPlayUpdate.InPlace, CarPlayUpdatePlanner.Plan(tabs, tabs with { Tabs = [new CarGridTemplate("B")] }));
        Assert.Equal(CarPlayUpdate.InPlace, CarPlayUpdatePlanner.Plan(search, search with { Hint = "Search" }));
        Assert.Equal(CarPlayUpdate.InPlace, CarPlayUpdatePlanner.Plan(message, message with { Message = "N", Title = "T" }));
    }

    [Fact]
    public void Different_types_need_replace() =>
        Assert.Equal(CarPlayUpdate.Replace, CarPlayUpdatePlanner.Plan(new CarListTemplate("A"), new CarGridTemplate("A")));

    // CPContactTemplate.Contact and the CPContact fields have setters
    [Fact]
    public void Contact_updates_in_place()
    {
        var contact = new CarPlayContactTemplate("Anna") { Id = "anna", Buttons = [new CarPlayContactButton(CarPlayContactButtonKind.Call)] };

        Assert.Equal(CarPlayUpdate.InPlace, CarPlayUpdatePlanner.Plan(contact, contact with { Name = "Anna Smith", Subtitle = "Mom", Buttons = [] }));
    }

    // Review Focus (Stage 4): switching state without closing the modal; other states require a new CPVoiceControlTemplate
    [Fact]
    public void Voice_control_state_switch_is_in_place_new_states_replace()
    {
        var voice = new CarVoiceControlTemplate([new("listening", "Listening"), new("thinking", "Thinking")]) { Id = "voice" };

        Assert.Equal(CarPlayUpdate.InPlace, CarPlayUpdatePlanner.Plan(voice, voice with { ActiveStateId = "thinking" }));
        Assert.Equal(CarPlayUpdate.InPlace, CarPlayUpdatePlanner.Plan(voice, voice with { States = [new("listening", "Listening"), new("thinking", "Thinking")] }));
        Assert.Equal(CarPlayUpdate.Replace, CarPlayUpdatePlanner.Plan(voice, voice with { States = [new("listening", "Listening…")] }));
    }

    // Stage 9: CPVoiceControlState.ActionButtons (iOS 26.4) has a setter: other action buttons are an in-place update,
    // but still not other states; the overlay (iOS 27) is a different presentation
    [Fact]
    public void Voice_with_other_actions_updates_in_place()
    {
        var voice = new CarVoiceControlTemplate([new("listening", "Listening") { Actions = [new CarAction("Stop") { Id = "stop" }] }]) { Id = "voice" };

        Assert.Equal(CarPlayUpdate.InPlace, CarPlayUpdatePlanner.Plan(voice, voice with { States = [new("listening", "Listening") { Actions = [new CarAction("Pause") { Id = "pause" }] }] }));
        Assert.Equal(CarPlayUpdate.InPlace, CarPlayUpdatePlanner.Plan(voice, voice with { States = [new("listening", "Listening")] }));
        Assert.Equal(CarPlayUpdate.Replace, CarPlayUpdatePlanner.Plan(voice, voice with { States = [new("listening", "Listening…") { Actions = voice.States[0].Actions }] }));
    }

    // Stage 9: CPListTemplate.ListHeader (iOS 26.4) has a setter: the header does not force replacing the list
    [Fact]
    public void List_with_other_header_updates_in_place()
    {
        var list = new CarListTemplate("Parking lots") { Id = "list" };

        Assert.Equal(CarPlayUpdate.InPlace, CarPlayUpdatePlanner.Plan(list, list with { DetailsHeader = new CarListDetailsHeader(CarImage.FromFile("a.png")) { Title = "Downtown" } }));
    }

    // Stage 5: CPNowPlayingTemplate.SharedTemplate is a singleton: the buttons, Up Next and Album/Artist have setters
    [Fact]
    public void Now_playing_updates_in_place()
    {
        var nowPlaying = new CarNowPlayingTemplate { Id = "np", Buttons = [new CarNowPlayingButton(CarNowPlayingButtonKind.Shuffle)] };

        Assert.Equal(CarPlayUpdate.InPlace, CarPlayUpdatePlanner.Plan(nowPlaying, nowPlaying with { Buttons = [], IsUpNextButtonEnabled = true, UpNextTitle = "Next" }));
    }
}
