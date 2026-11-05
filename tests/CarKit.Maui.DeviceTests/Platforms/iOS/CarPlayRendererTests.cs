using CarKit.Maui.Renderers;
using CarPlay;
using Foundation;
using UIKit;

namespace CarKit.Maui.DeviceTests;

// CarPlay renderers on the iOS simulator: CP* objects can be created without a CarPlay connection (spec: "Testing")
internal static class CarPlayRendererTests
{
    // 16×16 PNG
    private static readonly byte[] _png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAABAAAAAQCAIAAACQkWg2AAAAFklEQVR4nGOQW2BDEmIY1TCqYfhqAADLcvoBKIOPIQAAAABJRU5ErkJggg==");

    public static IReadOnlyList<DeviceTest> All() =>
    [
        new(nameof(List_renders_sections_rows_and_accessories), List_renders_sections_rows_and_accessories),
        new(nameof(List_image_row_renders_and_selects_images), List_image_row_renders_and_selects_images),
        new(nameof(List_loading_and_empty_view), List_loading_and_empty_view),
        new(nameof(List_update_during_spinner_keeps_template_and_completes), List_update_during_spinner_keeps_template_and_completes),
        new(nameof(List_structure_change_uses_update_sections), List_structure_change_uses_update_sections),
        new(nameof(List_item_values_can_be_cleared_in_place), List_item_values_can_be_cleared_in_place),
        new(nameof(List_title_change_needs_replace), List_title_change_needs_replace),
        new(nameof(Loading_list_with_sections_updates_in_place), Loading_list_with_sections_updates_in_place),
        new(nameof(Late_image_after_in_place_update_is_applied), Late_image_after_in_place_update_is_applied),
        new(nameof(Rows_keep_their_type_after_the_peers_are_collected), Rows_keep_their_type_after_the_peers_are_collected),
        new(nameof(Grid_renders_and_updates_in_place), Grid_renders_and_updates_in_place),
        new(nameof(Tab_renders_children_and_selects), Tab_renders_children_and_selects),
        new(nameof(Information_renders_layout_items_actions), Information_renders_layout_items_actions),
        new(nameof(Search_delegate_returns_results_and_submits), Search_delegate_returns_results_and_submits),
        new(nameof(Alert_action_closes_before_handler), Alert_action_closes_before_handler),
        new(nameof(Alert_keeps_the_first_actions_within_the_platform_limit), Alert_keeps_the_first_actions_within_the_platform_limit),
        new(nameof(Action_sheet_renders), Action_sheet_renders),
        new(nameof(Message_renders_as_information), Message_renders_as_information),
        new(nameof(Bar_buttons_render), Bar_buttons_render),
        new(nameof(Images_load_from_bytes_file_symbol_stream_and_uri), Images_load_from_bytes_file_symbol_stream_and_uri),
        new(nameof(Dark_variant_uses_image_asset_and_restyles), Dark_variant_uses_image_asset_and_restyles),
        new(nameof(Async_tinted_and_dark_images_load_off_the_main_thread), Async_tinted_and_dark_images_load_off_the_main_thread),
        new(nameof(Broken_image_renders_without_image), Broken_image_renders_without_image),
        new(nameof(Runtime_limits_are_positive_or_rule_defaults), Runtime_limits_are_positive_or_rule_defaults),
        new(nameof(Contact_renders_buttons_and_updates_in_place), Contact_renders_buttons_and_updates_in_place),
        new(nameof(Contact_image_from_stream_arrives_later), Contact_image_from_stream_arrives_later),
        new(nameof(Voice_control_renders_states_and_switches_state), Voice_control_renders_states_and_switches_state),
        new(nameof(Hooks_run_after_tab_info_and_after_updates), Hooks_run_after_tab_info_and_after_updates),
        new(nameof(Throwing_hook_is_reported_and_rendering_continues), Throwing_hook_is_reported_and_rendering_continues),
        new(nameof(Base_typed_hook_receives_any_template), Base_typed_hook_receives_any_template),
        new(nameof(Mismatched_hook_is_reported), Mismatched_hook_is_reported),
        new(nameof(Late_tab_image_keeps_the_hook_tab_image), Late_tab_image_keeps_the_hook_tab_image),
    ];

    private sealed class Fixture
    {
        public Fixture()
        {
            Context = new CarPlayRenderContext(Test.Session, Test, new CarImageLoader<UIImage>(new CarPlayImages(2), Test, combineDarkVariants: true), 2, () => Style, CloseAsync);
        }

        public TestSession Test { get; } = TestSession.CarPlay();

        public CarPlayRenderContext Context { get; }

        public CarContentStyle Style { get; set; }

        public List<string> Closed { get; } = [];

        private async Task CloseAsync(string id)
        {
            Closed.Add(id);
            await Test.Session.OnHostDismissedAsync(id);
        }
    }

    private static CPListItem Row(CPListTemplate list, int section, int index) => list.Sections[section].Item<CPListItem>(index);

    // An image from a stream that is opened only after the gate opens. Every open gives a new stream (the CarStreamImage
    // contract: the caller closes the stream): CarKit opens the source once per size, so the same image in a list row
    // (44 pt) and in a grid button (40 pt) is two reads. Stage 10: a MemoryStream shared from the gate was the cause of
    // the flakiness (one read closed the other's stream → ObjectDisposedException → no image).
    private static CarImage Gated(TaskCompletionSource gate) => CarImage.FromStream(async _ =>
    {
        await gate.Task;
        return new MemoryStream(_png);
    });

    // Stage 10: rows read from native objects do not depend on whether the GC collected the renderer state (in Release
    // locals do not hold it after their last use): without a live peer the Items2 binding gives
    // CPListTemplateItemWrapper (a symptom in several Release tests)
    private static Task Rows_keep_their_type_after_the_peers_are_collected()
    {
        var native = RenderedList();
        for (var i = 0; i < 3; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        Check.Equal("Parking", Row(native, 0, 0).Text);
        return Task.CompletedTask;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static CPListTemplate RenderedList() =>
        Check.Is<CPListTemplate>(CarPlayTemplateRenderers.Render(new CarListTemplate("List") { Sections = [new CarSection([new CarListItem("Parking")])] }, new Fixture().Context).Native);

    private static Task List_renders_sections_rows_and_accessories()
    {
        var f = new Fixture();
        var list = new CarListTemplate("Parking lots")
        {
            Sections =
            [
                new CarSection([
                    new CarListItem("Downtown") { Detail = "42 free", IsBrowsable = true },
                    new CarListItem("Track") { IsPlaying = true, PlaybackProgress = 0.5, IsExplicitContent = true, Accessory = CarListItemAccessory.Cloud },
                    new CarListItem("Disabled") { IsEnabled = false, OnSelected = _ => Task.CompletedTask },
                ]) { Header = "Nearby", IndexTitle = "N" },
                new CarSection([new CarListItem("Demo")]),
            ],
        };

        var native = Check.Is<CPListTemplate>(CarPlayTemplateRenderers.Render(list, f.Context).Native);

        Check.Equal("Parking lots", native.Title);
        Check.Equal(2, native.Sections.Length);
        Check.Equal("Nearby", native.Sections[0].Header);
        Check.Equal("N", native.Sections[0].SectionIndexTitle);
        Check.Equal("Downtown", Row(native, 0, 0).Text);
        Check.Equal("42 free", Row(native, 0, 0).DetailText);
        Check.Equal(CPListItemAccessoryType.DisclosureIndicator, Row(native, 0, 0).AccessoryType);
        Check.True(Row(native, 0, 1).IsPlaying);
        Check.Equal(0.5, (double)Row(native, 0, 1).PlaybackProgress);
        Check.True(Row(native, 0, 1).IsExplicitContent);
        Check.Equal(CPListItemAccessoryType.Cloud, Row(native, 0, 1).AccessoryType);
        Check.True(!Row(native, 0, 2).Enabled);
        Check.NotNull(Row(native, 0, 2).Handler);
        Check.Equal(list.Sections[0].Items[0].Id, (Row(native, 0, 0).UserInfo as NSString)?.ToString());
        Check.Equal(list.Id, (native.UserInfo as NSString)?.ToString());
        return Task.CompletedTask;
    }

    private static async Task List_image_row_renders_and_selects_images()
    {
        var f = new Fixture();
        var selected = -1;
        var row = new CarListImageRowItem("Photos", [CarImage.FromBytes(_png), CarImage.FromBytes(_png), CarImage.FromBytes(_png)])
        {
            OnImageSelected = context =>
            {
                selected = context.ImageIndex;
                return Task.CompletedTask;
            },
        };
        var native = Check.Is<CPListTemplate>(CarPlayTemplateRenderers.Render(new CarListTemplate("Gallery") { Sections = [new CarSection([row])] }, f.Context).Native);
        var item = native.Sections[0].Item<CPListImageRowItem>(0);
        var completed = false;

        item.ListImageRowHandler!(item, 1, () => completed = true);
        await Check.Eventually(() => completed, "image row completion");

        Check.Equal("Photos", item.Text);
        Check.Equal(1, selected);
    }

    private static Task List_loading_and_empty_view()
    {
        var f = new Fixture();
        var list = new CarListTemplate("Loading")
        {
            IsLoading = true,
            Sections = [new CarSection([new CarListItem("hidden")])],
            EmptyViewTitle = new CarText(["No parking lots", "None"]),
            EmptyViewSubtitle = "Try later",
        };

        var native = Check.Is<CPListTemplate>(CarPlayTemplateRenderers.Render(list, f.Context).Native);

        Check.Equal(0, native.Sections.Length);
        Check.Equal("None", native.EmptyViewTitleVariants[1]);
        Check.Equal("Try later", native.EmptyViewSubtitleVariants[0]);
        if (OperatingSystem.IsIOSVersionAtLeast(18, 4))
        {
            Check.True(native.ShowsSpinnerWhileEmpty);
        }

        return Task.CompletedTask;
    }

    // Review Focus 1 (mandatory scenario): a list update while an item's spinner is running
    private static async Task List_update_during_spinner_keeps_template_and_completes()
    {
        var f = new Fixture();
        var gate = new TaskCompletionSource();
        var started = false;
        CarPlayTemplateState? state = null;
        CarListTemplate? list = null;
        var row = new CarListItem("Parking") { Id = "row" };
        row = row with
        {
            OnSelected = async _ =>
            {
                started = true;
                var updated = list! with { Sections = [new CarSection([row with { Text = "Parking (reserving…)", Detail = "Wait" }])] };
                Check.Equal(CarPlayUpdate.InPlace, CarPlayTemplateRenderers.Update(state!, updated, f.Context));
                await gate.Task;
            },
        };
        list = new CarListTemplate("List") { Sections = [new CarSection([row])] };
        state = CarPlayTemplateRenderers.Render(list, f.Context);
        var native = Check.Is<CPListTemplate>(state.Native);
        var item = Row(native, 0, 0);
        var completed = false;

        item.Handler!(item, () => completed = true);
        await Check.Eventually(() => started, "row handler");
        await Task.Delay(100);
        Check.True(!completed);
        gate.SetResult();
        await Check.Eventually(() => completed, "spinner completion");

        Check.True(ReferenceEquals(native, state.Native));
        Check.True(ReferenceEquals(item, Row(native, 0, 0)));
        Check.Equal("Parking (reserving…)", item.Text);
        Check.Equal("Wait", item.DetailText);
    }

    private static Task List_structure_change_uses_update_sections()
    {
        var f = new Fixture();
        var a = new CarListItem("A");
        var list = new CarListTemplate("List") { Sections = [new CarSection([a])] };
        var state = CarPlayTemplateRenderers.Render(list, f.Context);
        var native = Check.Is<CPListTemplate>(state.Native);
        var first = Row(native, 0, 0);

        var result = CarPlayTemplateRenderers.Update(state, list with { Sections = [new CarSection([a, new CarListItem("B")]) { Header = "New" }] }, f.Context);

        Check.Equal(CarPlayUpdate.InPlace, result);
        Check.True(ReferenceEquals(native, state.Native));
        Check.Equal(2, native.Sections[0].Items2.Length);
        Check.Equal("New", native.Sections[0].Header);
        Check.True(!ReferenceEquals(first, Row(native, 0, 0)));
        return Task.CompletedTask;
    }

    // Detail, image and handler removed from the model → the CPListItem setters get nil (no binding exception)
    private static Task List_item_values_can_be_cleared_in_place()
    {
        var f = new Fixture();
        var row = new CarListItem("A") { Id = "a", Detail = "detail", Image = CarImage.FromBytes(_png), OnSelected = _ => Task.CompletedTask };
        var list = new CarListTemplate("List") { Sections = [new CarSection([row])] };
        var state = CarPlayTemplateRenderers.Render(list, f.Context);
        var native = Check.Is<CPListTemplate>(state.Native);
        var item = Row(native, 0, 0);

        var cleared = list with { Sections = [new CarSection([new CarListItem("A") { Id = "a" }])] };
        Check.Equal(CarPlayUpdate.InPlace, CarPlayTemplateRenderers.Update(state, cleared, f.Context));

        Check.True(ReferenceEquals(item, Row(native, 0, 0)));
        Check.True(string.IsNullOrEmpty(item.DetailText));
        Check.Null(item.Image);
        Check.Null(item.Handler);
        return Task.CompletedTask;
    }

    // I2 (review): a loading list has sections in the model but not in the CPListTemplate; an update must not look for its
    // rows
    private static Task Loading_list_with_sections_updates_in_place()
    {
        var f = new Fixture();
        var list = new CarListTemplate("Loading") { IsLoading = true, Sections = [new CarSection([new CarListItem("hidden") { Id = "hidden" }])] };
        var state = CarPlayTemplateRenderers.Render(list, f.Context);
        var native = Check.Is<CPListTemplate>(state.Native);

        Check.Equal(CarPlayUpdate.InPlace, CarPlayTemplateRenderers.Update(state, list with { EmptyViewTitle = "Still loading" }, f.Context));
        Check.Equal("Still loading", native.EmptyViewTitleVariants[0]);
        Check.Equal(CarPlayUpdate.InPlace, CarPlayTemplateRenderers.Update(state, list with { IsLoading = false }, f.Context));
        Check.Equal(1, native.Sections.Length);
        return Task.CompletedTask;
    }

    // I1 (review): a stream image that arrives after an in-place update (the same image in the model) must reach the row and
    // the button
    private static async Task Late_image_after_in_place_update_is_applied()
    {
        var f = new Fixture();
        var gate = new TaskCompletionSource();
        var image = Gated(gate);
        var row = new CarListItem("Parking") { Id = "row", Image = image };
        var list = new CarListTemplate("List") { Sections = [new CarSection([row])] };
        var grid = new CarGridTemplate("Grid") { Items = [new CarGridItem("Button", image) { Id = "button" }] };
        var listState = CarPlayTemplateRenderers.Render(list, f.Context);
        var gridState = CarPlayTemplateRenderers.Render(grid, f.Context);
        var native = Check.Is<CPListTemplate>(listState.Native);
        var gridNative = Check.Is<CPGridTemplate>(gridState.Native);
        var placeholder = gridNative.GridButtons[0].Image;

        Check.Equal(CarPlayUpdate.InPlace, CarPlayTemplateRenderers.Update(listState, list with { Sections = [new CarSection([row with { Detail = "3 free" }])] }, f.Context));
        Check.Equal(CarPlayUpdate.InPlace, CarPlayTemplateRenderers.Update(gridState, grid with { Title = "Grid 2" }, f.Context));
        gate.SetResult();

        await Check.Eventually(() => Row(native, 0, 0).Image is not null, "late list image");
        await Check.Eventually(() => !ReferenceEquals(gridNative.GridButtons[0].Image, placeholder), "late grid image");
    }

    // CPListTemplate.Title is read-only: the host replaces the template at the top of the stack or leaves the title deeper
    private static Task List_title_change_needs_replace()
    {
        var f = new Fixture();
        var list = new CarListTemplate("A") { Sections = [new CarSection([new CarListItem("1")])] };
        var state = CarPlayTemplateRenderers.Render(list, f.Context);
        var native = Check.Is<CPListTemplate>(state.Native);
        var renamed = list with { Title = "B", Sections = [new CarSection([new CarListItem("2")])] };

        Check.Equal(CarPlayUpdate.Replace, CarPlayTemplateRenderers.Update(state, renamed, f.Context));
        Check.Equal("1", Row(native, 0, 0).Text);
        Check.Equal(CarPlayUpdate.InPlace, CarPlayTemplateRenderers.Update(state, renamed, f.Context, keepFixed: true));
        Check.Equal("A", native.Title);
        Check.Equal("2", Row(native, 0, 0).Text);
        return Task.CompletedTask;
    }

    private static Task Grid_renders_and_updates_in_place()
    {
        var f = new Fixture();
        var grid = new CarGridTemplate("Services")
        {
            Items = [new CarGridItem(new CarText(["Parking", "Park"]), CarImage.FromBytes(_png)), new CarGridItem("Payment", CarImage.FromBytes(_png)) { IsEnabled = false }],
        };
        var state = CarPlayTemplateRenderers.Render(grid, f.Context);
        var native = Check.Is<CPGridTemplate>(state.Native);

        Check.Equal(2, native.GridButtons.Length);
        Check.Equal("Park", native.GridButtons[0].TitleVariants[1]);
        Check.True(!native.GridButtons[1].Enabled);
        Check.NotNull(native.GridButtons[0].Image);

        var updated = grid with { Title = "Other", Items = grid.Items.Add(new CarGridItem("Map", CarImage.FromBytes(_png))) };
        Check.Equal(CarPlayUpdate.InPlace, CarPlayTemplateRenderers.Update(state, updated, f.Context));
        Check.True(ReferenceEquals(native, state.Native));
        Check.Equal("Other", native.Title);
        Check.Equal(3, native.GridButtons.Length);
        return Task.CompletedTask;
    }

    private static async Task Tab_renders_children_and_selects()
    {
        var f = new Fixture();
        CarTemplate? selected = null;
        var list = new CarListTemplate("List") { TabInfo = new CarTabInfo("First", CarImage.FromSystemSymbol("list.bullet", null)) };
        var grid = new CarGridTemplate("Grid") { TabInfo = new CarTabInfo("Second") { Badge = 3 } };
        var tabs = new CarTabTemplate([list, grid])
        {
            OnTabSelected = context =>
            {
                selected = context.SelectedTab;
                return Task.CompletedTask;
            },
        };
        var state = CarPlayTemplateRenderers.Render(tabs, f.Context);
        var native = Check.Is<CPTabBarTemplate>(state.Native);

        Check.Equal(2, native.Templates.Length);
        Check.Equal("First", native.Templates[0].TabTitle);
        Check.NotNull(native.Templates[0].TabImage);
        Check.True(native.Templates[1].ShowsTabBadge);
        Check.Is<CPGridTemplate>(native.Templates[1]);
        native.Delegate!.DidSelectTemplate(native, native.Templates[1]);
        await Check.Eventually(() => selected is not null, "OnTabSelected");
        Check.Equal(grid.Id, selected!.Id);

        var secondTab = native.Templates[1];
        var updated = tabs with { Tabs = [list, grid with { Items = [new CarGridItem("X", CarImage.FromBytes(_png))] }, new CarListTemplate("Third")] };
        Check.Equal(CarPlayUpdate.InPlace, CarPlayTemplateRenderers.Update(state, updated, f.Context));
        Check.Equal(3, native.Templates.Length);
        Check.True(ReferenceEquals(secondTab, native.Templates[1]));
        Check.Equal(1, ((CPGridTemplate)native.Templates[1]).GridButtons.Length);
    }

    private static async Task Information_renders_layout_items_actions()
    {
        var f = new Fixture();
        var navigated = false;
        var info = new CarInformationTemplate("Downtown Parking")
        {
            Layout = CarInformationLayout.TwoColumn,
            Items = [new CarInformationItem("Price", "$6/h"), new CarInformationItem(null, "Open 24 h")],
            Actions =
            [
                new CarAction("Navigate", _ =>
                {
                    navigated = true;
                    return Task.CompletedTask;
                }) { Style = CarActionStyle.Primary },
                new CarAction("Call") { IsEnabled = false },
            ],
        };
        var state = CarPlayTemplateRenderers.Render(info, f.Context);
        var native = Check.Is<CPInformationTemplate>(state.Native);

        Check.Equal(CPInformationTemplateLayout.TwoColumn, native.Layout);
        Check.Equal(2, native.Items.Length);
        Check.Equal("$6/h", native.Items[0].Detail);
        Check.Equal(1, native.Actions.Length);
        Check.Equal(CPTextButtonStyle.Confirm, native.Actions[0].TextStyle);

        Check.Equal(CarPlayUpdate.InPlace, CarPlayTemplateRenderers.Update(state, info with { Title = "Another", Items = [new CarInformationItem("Price", "$8/h")] }, f.Context));
        Check.Equal("Another", native.Title);
        Check.Equal("$8/h", native.Items[0].Detail);
        Check.Equal(CarPlayUpdate.Replace, CarPlayTemplateRenderers.Update(state, info with { Layout = CarInformationLayout.Leading }, f.Context));

        // CPTextButton has no handler getter; select through the context the way the button handler does
        f.Context.Select(state, info.Actions[0]);
        await Check.Eventually(() => navigated, "action handler");
    }

    private static async Task Search_delegate_returns_results_and_submits()
    {
        var f = new Fixture();
        var opened = false;
        string? submitted = null;
        var search = new CarSearchTemplate
        {
            OnSearchTextChanged = context => Task.FromResult<IReadOnlyList<CarListItem>>(
            [
                new CarListItem($"{context.Text} – Downtown")
                {
                    OnSelected = _ =>
                    {
                        opened = true;
                        return Task.CompletedTask;
                    },
                },
                new CarListItem($"{context.Text} – North") { Image = CarImage.FromBytes(_png) },
            ]),
            OnSearchSubmitted = context =>
            {
                submitted = context.Text;
                return Task.CompletedTask;
            },
        };
        var native = Check.Is<CPSearchTemplate>(CarPlayTemplateRenderers.Render(search, f.Context).Native);
        CPListItem[]? results = null;

        native.Delegate!.UpdatedSearchText(native, "park", items => results = items);
        await Check.Eventually(() => results is not null, "search results");
        Check.Equal(2, results!.Length);
        Check.Equal("park – Downtown", results[0].Text);
        Check.NotNull(results[1].Image);

        var completed = false;
        native.Delegate.SelectedResult(native, results[0], () => completed = true);
        await Check.Eventually(() => completed && opened, "result selection");
        native.Delegate.SearchButtonPressed(native);
        await Check.Eventually(() => submitted == "park", "OnSearchSubmitted");
    }

    // Review Focus 2: the action handler sees the alert closed and can show the next one right away
    private static async Task Alert_action_closes_before_handler()
    {
        var f = new Fixture();
        CarTemplate? presentedInHandler = new CarAlertTemplate("sentinel");
        var second = new CarAlertTemplate("Confirmed");
        var alert = new CarAlertTemplate(new CarText(["Reserve a spot?", "Reserve?"]))
        {
            Actions =
            [
                new CarAction("Reserve", async context =>
                {
                    presentedInHandler = context.Session.Presented;
                    await context.Session.PresentAsync(second);
                }) { Style = CarActionStyle.Destructive },
                new CarAction("Cancel") { Style = CarActionStyle.Cancel },
            ],
        };
        await f.Test.Session.SetRootAsync(new CarListTemplate("Start"));
        await f.Test.Session.PresentAsync(alert);
        var native = Check.Is<CPAlertTemplate>(CarPlayTemplateRenderers.Render(alert, f.Context).Native);

        Check.Equal("Reserve?", native.TitleVariants[1]);
        Check.Equal(CPAlertActionStyle.Destructive, native.Actions[0].Style);
        Check.Equal(CPAlertActionStyle.Cancel, native.Actions[1].Style);
        native.Actions[0].Handler(native.Actions[0]);
        await Check.Eventually(() => ReferenceEquals(f.Test.Session.Presented, second), "second alert");

        Check.Null(presentedInHandler);
        Check.Equal(alert.Id, f.Closed.Single());
        Check.Equal(0, f.Test.Errors.Count);
    }

    // Stage 10: maximumActionCount depends on the entitlement (Audio 1, others 3), and when there are too many actions
    // CarPlay keeps the last ones
    private static Task Alert_keeps_the_first_actions_within_the_platform_limit()
    {
        var f = new Fixture();
        var limit = (int)CPAlertTemplate.MaximumActionCount;
        var alert = new CarAlertTemplate("Limit") { Actions = [.. Enumerable.Range(0, limit + 1).Select(i => new CarAction($"A{i}"))] };

        var native = Check.Is<CPAlertTemplate>(CarPlayTemplateRenderers.Render(alert, f.Context).Native);

        Check.Equal(limit, native.Actions.Length);
        Check.Equal("A0", native.Actions[0].Title);
        Check.Equal($"A{limit - 1}", native.Actions[^1].Title);
        Check.Equal<int?>(limit, CarPlayLimits.Read(CarLimitKind.Actions));
        return Task.CompletedTask;
    }

    private static Task Action_sheet_renders()
    {
        var f = new Fixture();
        var sheet = new CarActionSheetTemplate("Parking", "What to do?") { Actions = [new CarAction("Delete") { Style = CarActionStyle.Destructive }, new CarAction("Cancel")] };
        var untitled = new CarActionSheetTemplate(null, null) { Actions = [new CarAction("OK")] };

        var native = Check.Is<CPActionSheetTemplate>(CarPlayTemplateRenderers.Render(sheet, f.Context).Native);
        Check.Is<CPActionSheetTemplate>(CarPlayTemplateRenderers.Render(untitled, f.Context).Native);

        Check.Equal("Parking", native.Title);
        Check.Equal("What to do?", native.Message);
        Check.Equal(2, native.Actions.Length);
        return Task.CompletedTask;
    }

    private static Task Message_renders_as_information()
    {
        var f = new Fixture();
        var message = new CarMessageTemplate("Gate open") { Title = "Gate", Actions = [new CarAction("Close")] };
        var state = CarPlayTemplateRenderers.Render(message, f.Context);
        var native = Check.Is<CPInformationTemplate>(state.Native);

        Check.Equal("Gate", native.Title);
        Check.Equal("Gate open", native.Items.Single().Detail);
        Check.Equal(1, native.Actions.Length);
        Check.Equal(CarPlayUpdate.InPlace, CarPlayTemplateRenderers.Update(state, message with { Message = "Gate closed" }, f.Context));
        Check.Equal("Gate closed", native.Items.Single().Detail);
        return Task.CompletedTask;
    }

    private static Task Bar_buttons_render()
    {
        var f = new Fixture();
        var list = new CarListTemplate("List")
        {
            LeadingBarButtons = [new CarBarButton("Filter")],
            TrailingBarButtons = [new CarBarButton(CarImage.FromBytes(_png)) { IsEnabled = false }],
        };
        var native = Check.Is<CPListTemplate>(CarPlayTemplateRenderers.Render(list, f.Context).Native);

        Check.Equal("Filter", native.LeadingNavigationBarButtons.Single().Title);
        Check.NotNull(native.TrailingNavigationBarButtons.Single().Image);
        Check.True(!native.TrailingNavigationBarButtons.Single().Enabled);
        return Task.CompletedTask;
    }

    private static async Task Images_load_from_bytes_file_symbol_stream_and_uri()
    {
        var f = new Fixture();
        var file = Path.Combine(FileSystem.CacheDirectory, "carkit-uri.png");
        await File.WriteAllBytesAsync(file, _png);
        var list = new CarListTemplate("Images")
        {
            Sections =
            [
                new CarSection([
                    new CarListItem("bytes") { Image = CarImage.FromBytes(_png) },
                    new CarListItem("file") { Image = CarImage.FromFile("carkit_test.png") },
                    new CarListItem("symbol") { Image = CarImage.FromSystemSymbol("car.fill", null) },
                    new CarListItem("stream") { Image = CarImage.FromStream(_ => FileSystem.OpenAppPackageFileAsync("carkit_asset.png")) },
                    new CarListItem("uri") { Image = CarImage.FromUri(new Uri(file)) },
                ]),
            ],
        };
        var native = Check.Is<CPListTemplate>(CarPlayTemplateRenderers.Render(list, f.Context).Native);

        Check.NotNull(Row(native, 0, 0).Image);
        Check.NotNull(Row(native, 0, 1).Image);
        Check.NotNull(Row(native, 0, 2).Image);
        try
        {
            await Check.Eventually(() => Row(native, 0, 3).Image is not null && Row(native, 0, 4).Image is not null, "stream and uri images");
        }
        catch (CheckException ex)
        {
            throw new CheckException($"{ex.Message}; stream {Row(native, 0, 3).Image is not null}, uri {Row(native, 0, 4).Image is not null}, file {File.Exists(file)}; logs: {string.Join(" | ", f.Test.Logs)}");
        }
        var maximum = CPListItem.MaximumImageSize;
        var streamed = Row(native, 0, 3).Image!;
        Check.True(streamed.Size.Width <= maximum.Width + 0.5 && streamed.Size.Height <= maximum.Height + 0.5);
        Check.Equal(0, f.Test.Errors.Count);
    }

    private static Task Dark_variant_uses_image_asset_and_restyles()
    {
        var f = new Fixture { Style = CarContentStyle.Light };
        var image = CarImage.FromBytes(_png).WithDarkVariant(CarImage.FromSystemSymbol("moon.fill", null));
        var list = new CarListTemplate("Theme") { Sections = [new CarSection([new CarListItem("icon") { Image = image }])] };
        var state = CarPlayTemplateRenderers.Render(list, f.Context);
        var native = Check.Is<CPListTemplate>(state.Native);
        var light = Row(native, 0, 0).Image!;

        Check.NotNull(light.ImageAsset);
        f.Style = CarContentStyle.Dark;
        Check.Equal(CarPlayUpdate.InPlace, CarPlayTemplateRenderers.Update(state, state.Model, f.Context, keepFixed: true));
        Check.True(!ReferenceEquals(light, Row(native, 0, 0).Image));
        Check.Equal(CarContentStyle.Dark, state.ImageStyle);
        return Task.CompletedTask;
    }

    // Tint and the dark variant of stream/URI images are applied off the main thread
    private static async Task Async_tinted_and_dark_images_load_off_the_main_thread()
    {
        var f = new Fixture();
        var stream = CarImage.FromStream(_ => FileSystem.OpenAppPackageFileAsync("carkit_asset.png"));
        var list = new CarListTemplate("Asynchronous")
        {
            Sections =
            [
                new CarSection([
                    new CarListItem("tint") { Image = stream with { Tint = new CarColor(0, 128, 255) } },
                    new CarListItem("dark") { Image = CarImage.FromBytes(_png).WithDarkVariant(stream) },
                ]),
            ],
        };
        var native = Check.Is<CPListTemplate>(CarPlayTemplateRenderers.Render(list, f.Context).Native);

        await Check.Eventually(() => Row(native, 0, 0).Image is not null && Row(native, 0, 1).Image is not null, "async tinted and dark images");
        Check.NotNull(Row(native, 0, 1).Image!.ImageAsset);
        Check.Equal(0, f.Test.Logs.Count(l => l.StartsWith("Warning", StringComparison.Ordinal)));
    }

    // Review Focus 3: a broken image → no image and a log entry, no exception
    private static async Task Broken_image_renders_without_image()
    {
        var f = new Fixture();
        var list = new CarListTemplate("Broken")
        {
            Sections =
            [
                new CarSection([
                    new CarListItem("bytes") { Image = CarImage.FromBytes("not a PNG"u8) },
                    new CarListItem("missing file") { Image = CarImage.FromFile("missing.png") },
                    new CarListItem("stream") { Image = CarImage.FromStream(_ => throw new IOException("disk")) },
                ]),
            ],
        };
        var native = Check.Is<CPListTemplate>(CarPlayTemplateRenderers.Render(list, f.Context).Native);

        await Check.Eventually(() => f.Test.Logs.Count(l => l.StartsWith("Warning", StringComparison.Ordinal)) == 3, "three image warnings");
        Check.Null(Row(native, 0, 0).Image);
        Check.Null(Row(native, 0, 1).Image);
        Check.Null(Row(native, 0, 2).Image);
        Check.Equal(0, f.Test.Errors.Count);
    }

    private static Task Runtime_limits_are_positive_or_rule_defaults()
    {
        var test = TestSession.CarPlay();
        var limits = new CarRuntimeLimits(CarPlayLimits.Read, test);
        var capabilities = new CarCapabilities(CarPlatform.CarPlay, new Version(26, 0), null, limits.Get);
        foreach (var kind in new[] { CarLimitKind.ListItems, CarLimitKind.ListSections, CarLimitKind.GridItems, CarLimitKind.Tabs })
        {
            var value = capabilities.GetLimit(kind);
            Console.WriteLine($"INFO CarPlay limit {kind}: runtime {CarPlayLimits.Read(kind)}, effective {value}");
            Check.True(value > 0);
        }

        return Task.CompletedTask;
    }

    // Stage 4: CarPlayContactTemplate → CPContactTemplate; Message without PhoneOrEmail (validator in Warn/Off) is skipped
    private static async Task Contact_renders_buttons_and_updates_in_place()
    {
        var f = new Fixture();
        var calls = 0;
        var call = new CarPlayContactButton(CarPlayContactButtonKind.Call)
        {
            Id = "call",
            OnSelected = _ =>
            {
                calls++;
                return Task.CompletedTask;
            },
        };
        var directions = new CarPlayContactButton(CarPlayContactButtonKind.Directions) { Id = "directions" };
        var contact = new CarPlayContactTemplate("Anna Miller")
        {
            Subtitle = "Mom",
            InformativeText = "Last: yesterday",
            Image = CarImage.FromBytes(_png),
            Buttons =
            [
                call,
                new CarPlayContactButton(CarPlayContactButtonKind.Message) { PhoneOrEmail = "+15550100200" },
                directions,
                new CarPlayContactButton(CarPlayContactButtonKind.Custom) { Title = "Favorite", Image = CarImage.FromBytes(_png) },
                new CarPlayContactButton(CarPlayContactButtonKind.Message),
            ],
            TrailingBarButtons = [new CarBarButton("Edit")],
        };

        var state = CarPlayTemplateRenderers.Render(contact, f.Context);
        var native = Check.Is<CPContactTemplate>(state.Native);
        var card = native.Contact;
        var actions = Check.NotNull(card.Actions);

        Check.Equal("Anna Miller", card.Name);
        Check.Equal("Mom", card.Subtitle);
        Check.Equal("Last: yesterday", card.InformativeText);
        Check.NotNull(card.Image);
        Check.Equal(4, actions.Length);
        Check.Is<CPContactCallButton>(actions[0]);
        Check.Equal("+15550100200", Check.Is<CPContactMessageButton>(actions[1]).PhoneOrEmail);
        Check.Is<CPContactDirectionsButton>(actions[2]);
        Check.Equal("Favorite", actions[3].Title);
        Check.NotNull(actions[3].Image);
        Check.Equal(1, native.TrailingNavigationBarButtons.Length);
        Check.Equal(contact.Id, (native.UserInfo as NSString)?.ToString());
        f.Context.Select(state, call);
        await Check.Eventually(() => calls == 1, "call handler");

        var updated = contact with { Name = "Anna", Buttons = [call, directions] };
        Check.Equal(CarPlayUpdate.InPlace, CarPlayTemplateRenderers.Update(state, updated, f.Context));
        Check.True(ReferenceEquals(native, state.Native));
        Check.Equal("Anna", native.Contact.Name);
        Check.Equal(2, native.Contact.Actions!.Length);
        Check.Is<CPContactDirectionsButton>(native.Contact.Actions[1]);
        Check.Equal(0, f.Test.Errors.Count);
    }

    // CPButton.Image is read-only, CPContact.Image has a setter: after loading, a new CPContact with the image from the
    // cache
    private static async Task Contact_image_from_stream_arrives_later()
    {
        var f = new Fixture();
        var gate = new TaskCompletionSource();
        var image = Gated(gate);
        // I1 (review): the new CPContact created after the image must not lose what the hook set
        var contact = new CarPlayContactTemplate("Anna")
        {
            Image = image,
            Buttons = [new CarPlayContactButton(CarPlayContactButtonKind.Custom) { Title = "Favorite", Image = image }],
        }.ConfigureCarPlay(cp => cp.Contact.Subtitle = "From hook");
        var native = Check.Is<CPContactTemplate>(CarPlayTemplateRenderers.Render(contact, f.Context).Native);
        var placeholder = native.Contact.Actions![0].Image;
        var contactPlaceholder = native.Contact.Image;

        gate.SetResult();

        await Check.Eventually(() => !ReferenceEquals(contactPlaceholder, native.Contact.Image), "late contact image");
        Check.True(!ReferenceEquals(placeholder, native.Contact.Actions![0].Image));
        Check.Equal("Favorite", native.Contact.Actions[0].Title);
        Check.Equal("From hook", native.Contact.Subtitle);
    }

    // Stage 4: states in the constructor, switching the state in place (without closing the modal), different states =
    // replacement
    private static Task Voice_control_renders_states_and_switches_state()
    {
        var f = new Fixture();
        var voice = new CarVoiceControlTemplate([
            new CarVoiceControlState("listening", new CarText(["Listening…", "Listening"])) { Image = CarImage.FromSystemSymbol("mic.fill", null), Repeats = true },
            new CarVoiceControlState("thinking", "Thinking"),
        ]);

        var state = CarPlayTemplateRenderers.Render(voice, f.Context);
        var native = Check.Is<CPVoiceControlTemplate>(state.Native);

        Check.Equal(2, native.VoiceControlStates.Length);
        Check.Equal("listening", native.VoiceControlStates[0].Identifier);
        Check.Equal("Listening", native.VoiceControlStates[0].TitleVariants![1]);
        Check.NotNull(native.VoiceControlStates[0].Image);
        Check.True(native.VoiceControlStates[0].Repeats);
        Check.Null(native.VoiceControlStates[1].Image);
        Check.Equal("listening", native.ActiveStateIdentifier);

        Check.Equal(CarPlayUpdate.InPlace, CarPlayTemplateRenderers.Update(state, voice with { ActiveStateId = "thinking" }, f.Context));
        Check.True(ReferenceEquals(native, state.Native));
        Check.Equal("thinking", native.ActiveStateIdentifier);
        Check.Equal(CarPlayUpdate.Replace, CarPlayTemplateRenderers.Update(state, voice with { States = [new CarVoiceControlState("speaking", "Speaking")] }, f.Context));
        Check.Equal("thinking", native.ActiveStateIdentifier);
        return Task.CompletedTask;
    }

    // Review Focus 1–2 (Stage 4): the hook runs after the tab child's TabInfo and after every in-place update that
    // recreates what it changed (the list's EmptyViewTitleVariants, the grid's new CPBarButton)
    private static Task Hooks_run_after_tab_info_and_after_updates()
    {
        var f = new Fixture();
        var list = new CarListTemplate("List") { EmptyViewTitle = "From model" }.ConfigureCarPlay(cp => cp.EmptyViewTitleVariants = ["From hook"]);
        var listState = CarPlayTemplateRenderers.Render(list, f.Context);
        var listNative = Check.Is<CPListTemplate>(listState.Native);
        Check.Equal("From hook", listNative.EmptyViewTitleVariants[0]);
        Check.Equal(CarPlayUpdate.InPlace, CarPlayTemplateRenderers.Update(listState, list with { EmptyViewTitle = "New from model" }, f.Context));
        Check.Equal("From hook", listNative.EmptyViewTitleVariants[0]);

        var grid = new CarGridTemplate("Grid") { TrailingBarButtons = [new CarBarButton("A")] }
            .ConfigureCarPlay(cp => Array.ForEach(cp.TrailingNavigationBarButtons, button => button.ButtonStyle = CPBarButtonStyle.Rounded));
        var gridState = CarPlayTemplateRenderers.Render(grid, f.Context);
        var gridNative = Check.Is<CPGridTemplate>(gridState.Native);
        Check.Equal(CPBarButtonStyle.Rounded, gridNative.TrailingNavigationBarButtons[0].ButtonStyle);
        Check.Equal(CarPlayUpdate.InPlace, CarPlayTemplateRenderers.Update(gridState, grid with { TrailingBarButtons = [new CarBarButton("B")] }, f.Context));
        Check.Equal("B", gridNative.TrailingNavigationBarButtons[0].Title);
        Check.Equal(CPBarButtonStyle.Rounded, gridNative.TrailingNavigationBarButtons[0].ButtonStyle);

        var tab = new CarListTemplate("List") { TabInfo = new CarTabInfo("From model") }.ConfigureCarPlay(cp => cp.TabTitle = "From hook");
        var tabs = new CarTabTemplate([tab, new CarGridTemplate("Grid") { TabInfo = new CarTabInfo("Grid") }]);
        var tabsState = CarPlayTemplateRenderers.Render(tabs, f.Context);
        var tabsNative = Check.Is<CPTabBarTemplate>(tabsState.Native);
        Check.Equal("From hook", tabsNative.Templates[0].TabTitle);
        Check.Equal(CarPlayUpdate.InPlace, CarPlayTemplateRenderers.Update(tabsState, tabs with { Tabs = [tab with { TabInfo = new CarTabInfo("New from model") }, tabs.Tabs[1]] }, f.Context));
        Check.Equal("From hook", tabsNative.Templates[0].TabTitle);
        Check.Equal(0, f.Test.Errors.Count);
        return Task.CompletedTask;
    }

    // Review Focus 3 (Stage 4): a hook exception → ICarErrorHandler, the next hook and the template are still created
    private static Task Throwing_hook_is_reported_and_rendering_continues()
    {
        var f = new Fixture();
        var second = false;
        var list = new CarListTemplate("List")
            .ConfigureCarPlay(_ => throw new InvalidOperationException("hook"))
            .ConfigureCarPlay(_ => second = true);

        var native = Check.Is<CPListTemplate>(CarPlayTemplateRenderers.Render(list, f.Context).Native);

        Check.Equal("List", native.Title);
        Check.True(second);
        Check.Equal("hook", Check.Is<InvalidOperationException>(f.Test.Errors.Single()).Message);
        return Task.CompletedTask;
    }

    // Action<CPTemplate> from a manual PlatformHooks.Add fits every template (contravariance), including the new ones from
    // Stage 4
    private static Task Base_typed_hook_receives_any_template()
    {
        var f = new Fixture();
        var seen = new List<string>();
        CarTemplate[] templates =
        [
            new CarListTemplate("List"),
            new CarAlertTemplate("Alert"),
            new CarPlayContactTemplate("Anna"),
            new CarVoiceControlTemplate([new CarVoiceControlState("listening", "Listening")]),
        ];

        foreach (var template in templates)
        {
            var hooked = template with { PlatformHooks = template.PlatformHooks.Add(CarPlatform.CarPlay, new Action<CPTemplate>(cp => seen.Add(cp.GetType().Name))) };
            CarPlayTemplateRenderers.Render(hooked, f.Context);
        }

        Check.Equal("CPListTemplate,CPAlertTemplate,CPContactTemplate,CPVoiceControlTemplate", string.Join(',', seen));
        return Task.CompletedTask;
    }

    private static Task Mismatched_hook_is_reported()
    {
        var f = new Fixture();
        var list = new CarListTemplate("List") { PlatformHooks = CarPlatformHooks.None.Add(CarPlatform.CarPlay, new Action<CPGridTemplate>(_ => { })) };

        Check.Is<CPListTemplate>(CarPlayTemplateRenderers.Render(list, f.Context).Native);

        Check.Is<InvalidOperationException>(f.Test.Errors.Single());
        return Task.CompletedTask;
    }

    // I1 (review): a stream TabImage arrives after the render; the tab child's hook runs again and its TabImage wins
    private static async Task Late_tab_image_keeps_the_hook_tab_image()
    {
        var f = new Fixture();
        var gate = new TaskCompletionSource<Stream>();
        var hookImage = UIImage.GetSystemImage("star.fill")!;
        var hookRuns = 0;
        var tab = new CarListTemplate("List") { TabInfo = new CarTabInfo("List", CarImage.FromStream(_ => gate.Task)) }
            .ConfigureCarPlay(cp =>
            {
                hookRuns++;
                cp.TabImage = hookImage;
            });
        var tabs = new CarTabTemplate([tab, new CarGridTemplate("Grid") { TabInfo = new CarTabInfo("Grid") }]);
        var native = Check.Is<CPTabBarTemplate>(CarPlayTemplateRenderers.Render(tabs, f.Context).Native);
        Check.Equal(1, hookRuns);

        gate.SetResult(new MemoryStream(_png));

        await Check.Eventually(() => hookRuns == 2, "hook after the late tab image");
        Check.True(ReferenceEquals(hookImage, native.Templates[0].TabImage));
    }
}
