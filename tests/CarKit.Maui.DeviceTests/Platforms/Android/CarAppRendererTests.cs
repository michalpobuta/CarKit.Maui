using Android.OS;
using Android.Runtime;
using AndroidX.Car.App;
using AndroidX.Car.App.Model;
using AndroidX.Car.App.Model.SignIn;
using AndroidX.Car.App.Serialization;
using AndroidX.Core.Graphics.Drawable;
using CarKit.Maui.Renderers;
using CarKitText = CarKit.Maui.CarText;
using NativeAction = AndroidX.Car.App.Model.Action;
using NativeCarText = AndroidX.Car.App.Model.CarText;

namespace CarKit.Maui.DeviceTests;

// Car App Library renderers on the emulator: models and builders are plain Java objects, no Android Auto host
// (spec: "Testing")
internal static class CarAppRendererTests
{
    // 16×16 PNG
    private static readonly byte[] _png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAABAAAAAQCAIAAACQkWg2AAAAFklEQVR4nGOQW2BDEmIY1TCqYfhqAADLcvoBKIOPIQAAAABJRU5ErkJggg==");
    private static readonly Handler _main = new(Looper.MainLooper!);
    private static readonly string[] _systemIcons = ["ic_menu_search", "ic_dialog_map", "ic_menu_compass", "star_big_on", "ic_lock_power_off"];

    public static IReadOnlyList<DeviceTest> All() =>
    [
        new(nameof(List_renders_rows_toggle_browsable), List_renders_rows_toggle_browsable),
        new(nameof(Row_click_invokes_handler), Row_click_invokes_handler),
        new(nameof(Toggle_change_invokes_handler), Toggle_change_invokes_handler),
        new(nameof(Sectioned_list_renders_row_and_grid_sections), Sectioned_list_renders_row_and_grid_sections),
        new(nameof(Sectioned_list_keeps_its_type_from_loading_to_loaded), Sectioned_list_keeps_its_type_from_loading_to_loaded),
        new(nameof(Loading_placeholder_matches_the_list_template_type), Loading_placeholder_matches_the_list_template_type),
        new(nameof(List_below_api_8_flattens_or_sections), List_below_api_8_flattens_or_sections),
        new(nameof(Grid_renders_loading_items_for_pending_images), Grid_renders_loading_items_for_pending_images),
        new(nameof(Tab_renders_active_tab_and_callback_switches), Tab_renders_active_tab_and_callback_switches),
        new(nameof(Pane_renders_rows_and_actions), Pane_renders_rows_and_actions),
        new(nameof(Search_callback_updates_results), Search_callback_updates_results),
        new(nameof(Screen_state_reset_forgets_tab_and_search), Screen_state_reset_forgets_tab_and_search),
        new(nameof(Message_and_long_message_render), Message_and_long_message_render),
        new(nameof(Modal_message_action_closes_before_handler), Modal_message_action_closes_before_handler),
        new(nameof(Modal_screen_wired_like_the_host_closes_on_action), Modal_screen_wired_like_the_host_closes_on_action),
        new(nameof(Clickable_items_with_pending_images_build), Clickable_items_with_pending_images_build),
        new(nameof(Legacy_action_strip_keeps_one_titled_action), Legacy_action_strip_keeps_one_titled_action),
        new(nameof(Alert_renders_with_icon_and_closes_before_handler), Alert_renders_with_icon_and_closes_before_handler),
        new(nameof(Header_and_action_strip_by_api_level), Header_and_action_strip_by_api_level),
        new(nameof(Detail_change_shape_is_refresh_row_change_is_not), Detail_change_shape_is_refresh_row_change_is_not),
        new(nameof(Images_from_resource_bytes_asset_and_symbol), Images_from_resource_bytes_asset_and_symbol),
        new(nameof(Sign_in_renders_pin_qr_and_provider), Sign_in_renders_pin_qr_and_provider),
        new(nameof(Input_sign_in_submits_text_to_the_current_handler), Input_sign_in_submits_text_to_the_current_handler),
        new(nameof(Hooks_configure_builders_before_build), Hooks_configure_builders_before_build),
        new(nameof(Throwing_hook_is_reported_and_rendering_continues), Throwing_hook_is_reported_and_rendering_continues),
        new(nameof(Hooks_for_another_builder_are_reported_once), Hooks_for_another_builder_are_reported_once),
    ];

    private sealed class Fixture
    {
        public Fixture(int apiLevel = 8, bool sectioned = false)
        {
            Test = TestSession.AndroidAuto(apiLevel);
            var app = Android.App.Application.Context;
            Context = new CarAppRenderContext(
                Test.Session, Test, apiLevel, app, new CarImageLoader<CarIcon>(new CarAppImages(app), Test, combineDarkVariants: false),
                () => CarContentStyle.Light, work => _main.Post(work), sectioned, Test);
            Screen = new CarAppScreenState { IsRoot = true, Rerender = () => Rerenders++ };
        }

        public TestSession Test { get; }

        public CarAppRenderContext Context { get; }

        public CarAppScreenState Screen { get; }

        public int Rerenders { get; set; }

        public CarAppRendered Render(CarTemplate template, CarAppScreenState? screen = null) =>
            CarAppTemplateRenderers.Render(template, Context, screen ?? Screen);
    }

    private sealed class Done : Java.Lang.Object, IOnDoneCallback
    {
        public bool Succeeded { get; private set; }

        public void OnFailure(Bundleable? response)
        {
        }

        public void OnSuccess(Bundleable? response) => Succeeded = true;
    }

    private static string? Text(NativeCarText? text) => text?.ToCharSequence();

    private static List<T> Items<T>(ItemList list) where T : Java.Lang.Object => [.. list.Items!.Select(item => ((IJavaObject)item).JavaCast<T>()!)];

    private static Task List_renders_rows_toggle_browsable()
    {
        var f = new Fixture(apiLevel: 7);
        var list = new CarListTemplate("Parking lots")
        {
            Sections =
            [
                new CarSection([
                    new CarListItem("Downtown") { Detail = "42 free", IsBrowsable = true, Image = CarImage.FromBytes(_png), OnSelected = _ => Task.CompletedTask },
                    new CarListItem("Wi-Fi") { Toggle = new CarListItemToggle(true), OnSelected = _ => Task.CompletedTask },
                    new CarListItem("Disabled") { IsEnabled = false, OnSelected = _ => Task.CompletedTask },
                ]),
            ],
            TrailingBarButtons = [new CarBarButton("Filter")],
        };

        var rendered = f.Render(list);
        var template = Check.Is<ListTemplate>(rendered.Template);
        var rows = Items<Row>(template.SingleList!);

        Check.Equal("Parking lots", Text(template.Header!.Title));
        Check.Equal(NativeAction.TypeAppIcon, template.Header.StartHeaderAction!.Type);
        Check.Equal(1, template.Header.EndHeaderActions!.Count);
        Check.Equal(3, rows.Count);
        Check.Equal("Downtown", Text(rows[0].Title));
        Check.Equal("42 free", Text(rows[0].Texts![0]));
        Check.True(rows[0].IsBrowsable);
        Check.NotNull(rows[0].OnClickDelegate);
        Check.NotNull(rows[0].Image);
        Check.True(rows[1].Toggle!.IsChecked);
        Check.Null(rows[1].OnClickDelegate);
        Check.True(!rows[2].IsEnabled);
        Check.Null(rows[2].OnClickDelegate);
        Check.Equal(new CarAppRowShape("Wi-Fi", Toggle: true), rendered.Shape.Sections[0].Rows[1]);
        return Task.CompletedTask;
    }

    private static async Task Row_click_invokes_handler()
    {
        var f = new Fixture();
        var selected = false;
        var list = new CarListTemplate("List")
        {
            Sections = [new CarSection([new CarListItem("A") { OnSelected = _ => { selected = true; return Task.CompletedTask; } }])],
        };
        var template = Check.Is<ListTemplate>(f.Render(list).Template);
        var row = Items<Row>(template.SingleList!)[0];

        row.OnClickDelegate!.SendClick(new Done());
        await Check.Eventually(() => selected, "row handler");
    }

    private static async Task Toggle_change_invokes_handler()
    {
        var f = new Fixture();
        bool? toggled = null;
        var list = new CarListTemplate("Settings")
        {
            Sections = [new CarSection([new CarListItem("Wi-Fi") { Toggle = new CarListItemToggle(true) { OnToggled = c => { toggled = c.IsChecked; return Task.CompletedTask; } } }])],
        };
        var template = Check.Is<ListTemplate>(f.Render(list).Template);
        var row = Items<Row>(template.SingleList!)[0];

        row.Toggle!.OnCheckedChangeDelegate!.SendCheckedChange(false, new Done());
        await Check.Eventually(() => toggled == false, "toggle handler");
    }

    private static async Task Sectioned_list_renders_row_and_grid_sections()
    {
        var f = new Fixture(apiLevel: 8, sectioned: true);
        var image = -1;
        var list = new CarListTemplate("Gallery")
        {
            Sections =
            [
                new CarSection([
                    new CarListItem("A1"),
                    new CarListItem("A2"),
                    new CarListImageRowItem("Photos", [CarImage.FromBytes(_png), CarImage.FromFile("carkit_test.png")])
                    {
                        OnImageSelected = c => { image = c.ImageIndex; return Task.CompletedTask; },
                    },
                    new CarListItem("A3"),
                ]) { Header = "Section A", IndexTitle = "A" },
            ],
        };

        var rendered = f.Render(list);
        var template = Check.Is<SectionedItemTemplate>(rendered.Template);
        var sections = template.Sections!.Select(s => ((IJavaObject)s).JavaCast<Section>()!).ToList();

        Check.Equal("Gallery", Text(template.Header!.Title));
        Check.Equal(3, sections.Count);
        Check.Equal("Section A", Text(sections[0].Title));
        Check.Equal(2, sections[0].ItemsDelegate!.Size);
        Check.True(((IJavaObject)template.Sections![1]).JavaCast<Java.Lang.Object>()!.Class!.SimpleName == "GridSection");
        Check.Equal("Photos", Text(sections[1].Title));
        Check.Equal(2, sections[1].ItemsDelegate!.Size);
        Check.Null(Text(sections[2].Title));
        Check.True(template.IsAlphabeticalIndexingAllowed);
        Check.Equal(3, rendered.Shape.Sections.Count);
        Check.Equal(CarAppTemplateTypes.Sectioned, rendered.Shape.Type);
        await Task.CompletedTask;
        Check.Equal(-1, image);
    }

    private static Task Sectioned_list_keeps_its_type_from_loading_to_loaded()
    {
        var f = new Fixture(apiLevel: 8, sectioned: true);
        var loading = f.Render(new CarListTemplate("List") { IsLoading = true });
        var loaded = f.Render(new CarListTemplate("List") { Sections = [new CarSection([new CarListItem("A")])] });
        var empty = f.Render(new CarListTemplate("List") { EmptyViewTitle = "Empty" });

        Check.True(Check.Is<SectionedItemTemplate>(loading.Template).IsLoading);
        Check.Is<SectionedItemTemplate>(loaded.Template);
        Check.Equal(1, Check.Is<SectionedItemTemplate>(empty.Template).Sections!.Count);
        Check.True(CarAppRefreshRules.IsRefresh(loading.Shape, loaded.Shape));
        return Task.CompletedTask;
    }

    // M2 (review): the loading screen before SetRootAsync has the list type of this connection, so a list root is a refresh
    // (no step)
    private static Task Loading_placeholder_matches_the_list_template_type()
    {
        var f = new Fixture(apiLevel: 8, sectioned: true);
        var app = Android.App.Application.Context;
        var sectioned = CarAppTemplateRenderers.Loading(app, 8, useSectionedItemTemplate: true);
        var list = CarAppTemplateRenderers.Loading(app, 7, useSectionedItemTemplate: false);
        var root = f.Render(new CarListTemplate("Menu") { Sections = [new CarSection([new CarListItem("A")])] });

        Check.True(Check.Is<SectionedItemTemplate>(sectioned.Template).IsLoading);
        Check.True(Check.Is<ListTemplate>(list.Template).IsLoading);
        Check.True(CarAppRefreshRules.IsRefresh(sectioned.Shape, root.Shape));
        return Task.CompletedTask;
    }

    private static Task List_below_api_8_flattens_or_sections()
    {
        var f = new Fixture(apiLevel: 6);
        var screen = new CarAppScreenState();
        var headed = new CarListTemplate("List")
        {
            Sections = [new CarSection([new CarListItem("1")]) { Header = "A" }, new CarSection([new CarListItem("2"), new CarListImageRowItem("Images", [CarImage.FromBytes(_png)])]) { Header = "B" }],
        };
        var mixed = headed with { Sections = [headed.Sections[0], headed.Sections[1] with { Header = null }] };

        var sectioned = Check.Is<ListTemplate>(f.Render(headed, screen).Template);
        var flat = Check.Is<ListTemplate>(f.Render(mixed, screen).Template);

#pragma warning disable CS0618 // Older hosts (API < 7): title and header action without Header
        Check.Equal("List", Text(sectioned.Title));
        Check.Equal(NativeAction.TypeBack, sectioned.HeaderAction!.Type);
#pragma warning restore CS0618
        Check.Equal(2, sectioned.SectionedLists!.Count);
        Check.Equal(3, flat.SingleList!.Items!.Count);
        var imageRow = Items<Row>(flat.SingleList)[2];
        Check.Equal("Images", Text(imageRow.Title));
        Check.NotNull(imageRow.Image);
        return Task.CompletedTask;
    }

    private static async Task Grid_renders_loading_items_for_pending_images()
    {
        var f = new Fixture(apiLevel: 8);
        var grid = new CarGridTemplate("Services")
        {
            Items =
            [
                new CarGridItem("Bytes", CarImage.FromBytes(_png)) { Text = "description" },
                new CarGridItem("Stream", CarImage.FromStream(async _ => await FileSystem.OpenAppPackageFileAsync("carkit_asset.png"))),
                new CarGridItem("Broken", CarImage.FromBytes("not PNG"u8)),
            ],
        };

        var first = Items<GridItem>(Check.Is<GridTemplate>(f.Render(grid).Template).SingleList!);
        Check.NotNull(first[0].Image);
        Check.Equal("description", Text(first[0].Text));
        Check.True(first[1].IsLoading);
        Check.Null(first[1].Image);
        Check.NotNull(first[2].Image);

        await Check.Eventually(() => f.Rerenders > 0, "rerender after image load");
        var second = Items<GridItem>(Check.Is<GridTemplate>(f.Render(grid).Template).SingleList!);
        Check.True(!second[1].IsLoading);
        Check.NotNull(second[1].Image);
    }

    private static async Task Tab_renders_active_tab_and_callback_switches()
    {
        var f = new Fixture(apiLevel: 8, sectioned: true);
        CarTemplate? selected = null;
        var list = new CarListTemplate("List") { Sections = [new CarSection([new CarListItem("A")])], TabInfo = new CarTabInfo("First") };
        var grid = new CarGridTemplate("Grid") { Items = [new CarGridItem("X", CarImage.FromBytes(_png))], TabInfo = new CarTabInfo("Second", CarImage.FromBytes(_png)) };
        var tabs = new CarTabTemplate([list, grid]) { OnTabSelected = c => { selected = c.SelectedTab; return Task.CompletedTask; } };

        var first = f.Render(tabs);
        var template = Check.Is<TabTemplate>(first.Template);
        Check.Equal(2, template.Tabs!.Count);
        Check.Equal(list.Id, template.ActiveTabContentId);
        Check.Equal(NativeAction.TypeAppIcon, template.HeaderAction!.Type);
        Check.NotNull(((IJavaObject)template.Tabs[0]).JavaCast<AndroidX.Car.App.Model.Tab>()!.Icon);
        Check.Is<SectionedItemTemplate>(template.TabContents!.Template);
        Check.Null(Check.Is<SectionedItemTemplate>(template.TabContents.Template).Header);

        template.TabCallbackDelegate!.SendTabSelected(grid.Id, new Done());
        await Check.Eventually(() => selected is not null && f.Rerenders > 0, "tab selection");
        Check.Equal(grid.Id, selected!.Id);
        Check.Equal(grid.Id, f.Screen.UserSelectedTab);

        var second = f.Render(tabs);
        Check.Equal(grid.Id, Check.Is<TabTemplate>(second.Template).ActiveTabContentId);
        Check.Is<GridTemplate>(Check.Is<TabTemplate>(second.Template).TabContents!.Template);
        Check.True(CarAppRefreshRules.IsRefresh(first.Shape, second.Shape));
    }

    private static Task Pane_renders_rows_and_actions()
    {
        var f = new Fixture(apiLevel: 8);
        var info = new CarInformationTemplate("Parking")
        {
            Items = [new CarInformationItem("Price", "$6/h"), new CarInformationItem(null, "Open 24 h")],
            Actions = [new CarAction("Navigate") { Style = CarActionStyle.Primary }, new CarAction("Call")],
        };

        var pane = Check.Is<PaneTemplate>(f.Render(info).Template);
        var rows = pane.Pane!.Rows!.Select(r => ((IJavaObject)r).JavaCast<Row>()!).ToList();
        var empty = Check.Is<PaneTemplate>(f.Render(new CarInformationTemplate("Empty")).Template);
        var loading = Check.Is<PaneTemplate>(f.Render(new CarInformationTemplate("Loading") { IsLoading = true }).Template);

        Check.Equal("Parking", Text(pane.Header!.Title));
        Check.Equal(2, rows.Count);
        Check.Equal("Price", Text(rows[0].Title));
        Check.Equal("$6/h", Text(rows[0].Texts![0]));
        Check.Equal("Open 24 h", Text(rows[1].Title));
        Check.Equal(2, pane.Pane.Actions!.Count);
        Check.True((((IJavaObject)pane.Pane.Actions[0]).JavaCast<NativeAction>()!.Flags & NativeAction.FlagPrimary) != 0);
        Check.Equal("Empty", Text(((IJavaObject)empty.Pane!.Rows![0]).JavaCast<Row>()!.Title));
        Check.True(loading.Pane!.IsLoading);
        return Task.CompletedTask;
    }

    private static async Task Search_callback_updates_results()
    {
        var f = new Fixture(apiLevel: 8);
        string? submitted = null;
        var search = new CarSearchTemplate
        {
            Hint = "Search parking",
            InitialText = "par",
            OnSearchTextChanged = c => Task.FromResult<IReadOnlyList<CarListItem>>([new CarListItem($"{c.Text} 1"), new CarListItem($"{c.Text} 2")]),
            OnSearchSubmitted = c => { submitted = c.Text; return Task.CompletedTask; },
        };

        var template = Check.Is<SearchTemplate>(f.Render(search).Template);
        Check.Equal("Search parking", template.SearchHint);
        Check.Equal("par", template.InitialSearchText);
        Check.Equal(0, template.ItemList!.Items!.Count);

        template.SearchCallbackDelegate!.SendSearchTextChanged("park", new Done());
        await Check.Eventually(() => f.Screen.SearchResults.Count == 2 && f.Rerenders > 0, "search results");
        var refreshed = Check.Is<SearchTemplate>(f.Render(search).Template);
        Check.Equal(2, refreshed.ItemList!.Items!.Count);
        Check.Equal("park", refreshed.InitialSearchText);
        refreshed.SearchCallbackDelegate!.SendSearchSubmitted("park", new Done());
        await Check.Eventually(() => submitted == "park", "OnSearchSubmitted");
    }

    // SetRootAsync with a different root on the same screen: the previous model's tab selection and search results are gone
    private static Task Screen_state_reset_forgets_tab_and_search()
    {
        var f = new Fixture(apiLevel: 8);
        var tabs = new CarTabTemplate([new CarListTemplate("A") { Id = "a" }, new CarListTemplate("B") { Id = "b" }]);
        f.Screen.UserSelectedTab = "b";
        f.Screen.SearchResults = [new CarListItem("old result")];
        f.Screen.Search.Begin("old");
        Check.Equal("b", Check.Is<TabTemplate>(f.Render(tabs).Template).ActiveTabContentId);

        f.Screen.Reset();

        Check.Equal("a", Check.Is<TabTemplate>(f.Render(tabs).Template).ActiveTabContentId);
        Check.Equal(0, f.Screen.SearchResults.Count);
        Check.Equal(string.Empty, f.Screen.Search.Text);
        return Task.CompletedTask;
    }

    private static Task Message_and_long_message_render()
    {
        var f = new Fixture(apiLevel: 8);
        var message = new CarMessageTemplate("Gate open") { Title = "Gate", Icon = CarImage.FromBytes(_png), Actions = [new CarAction("Close"), new CarAction("OK")] };
        var native = Check.Is<MessageTemplate>(f.Render(message).Template);
        var loading = Check.Is<MessageTemplate>(f.Render(message with { IsLoading = true }).Template);
        var longMessage = Check.Is<LongMessageTemplate>(f.Render(new CarLongMessageTemplate("Terms", "Long text")
        {
            Actions = [new CarAction("Accept")],
            TrailingBarButtons = [new CarBarButton("Help")],
        }).Template);

        Check.Equal("Gate open", Text(native.Message));
        Check.Equal("Gate", Text(native.Header!.Title));
        Check.NotNull(native.Icon);
        Check.Equal(2, native.Actions!.Count);
        Check.True(loading.IsLoading);
        Check.Null(loading.Icon);
        Check.Equal("Long text", Text(longMessage.Message));
        Check.Equal("Terms", Text(longMessage.Title));
        Check.Equal(1, longMessage.Actions!.Count);
        return Task.CompletedTask;
    }

    // Modal unification (Stage 3): an action of a modal screen (ActionSheet → Message on Android Auto) closes it before the
    // handler
    private static async Task Modal_message_action_closes_before_handler()
    {
        var f = new Fixture(apiLevel: 8);
        CarTemplate? presentedInHandler = new CarMessageTemplate("sentinel");
        var closed = 0;
        var sheet = new CarActionSheetTemplate("Parking", "Delete the reservation?")
        {
            Actions = [new CarAction("Delete", c => { presentedInHandler = c.Session.Presented; return Task.CompletedTask; })],
        };
        await f.Test.Session.SetRootAsync(new CarListTemplate("Start"));
        await f.Test.Session.PresentAsync(sheet);
        var screen = new CarAppScreenState
        {
            IsModal = true,
            CloseModal = async () =>
            {
                closed++;
                await f.Test.Session.OnHostDismissedAsync(sheet.Id);
            },
        };
        var fallback = Check.Is<CarMessageTemplate>(CarTemplateFallbacks.Create(sheet));

        var native = Check.Is<MessageTemplate>(f.Render(fallback, screen).Template);
        ((IJavaObject)native.Actions![0]).JavaCast<NativeAction>()!.OnClickDelegate!.SendClick(new Done());
        await Check.Eventually(() => presentedInHandler is null, "modal action handler");

        Check.Equal(1, closed);
        Check.Equal(NativeAction.TypeBack, native.Header!.StartHeaderAction!.Type);
    }

    // C1 (review): the host assigns CloseModal after the screen is created, that is after the first render; the action must
    // read it on click
    private static async Task Modal_screen_wired_like_the_host_closes_on_action()
    {
        var f = new Fixture(apiLevel: 8);
        var closed = 0;
        var handled = false;
        var message = new CarMessageTemplate("Delete?") { Actions = [new CarAction("Delete", _ => { handled = true; return Task.CompletedTask; })] };
        var screen = new CarAppScreenState { IsModal = true };
        var native = Check.Is<MessageTemplate>(f.Render(message, screen).Template);
        screen.CloseModal = () =>
        {
            closed++;
            return Task.CompletedTask;
        };

        ((IJavaObject)native.Actions![0]).JavaCast<NativeAction>()!.OnClickDelegate!.SendClick(new Done());
        await Check.Eventually(() => handled, "modal action handler");
        Check.Equal(1, closed);
    }

    // C2 (review): a GridItem in the loading state must not have an OnClickListener (GridItem.Builder.build)
    private static async Task Clickable_items_with_pending_images_build()
    {
        var f = new Fixture(apiLevel: 8, sectioned: true);
        var gate = new TaskCompletionSource();
        // Stage 10: a new stream on every open: the grid (160 px) and the image row (128 px) are two reads of the same
        // source; a stream shared from the gate was closed after the first read (like the flaky CarPlay images test)
        var pending = CarImage.FromStream(async _ =>
        {
            await gate.Task;
            return new MemoryStream(_png);
        });
        var grid = new CarGridTemplate("Services") { Items = [new CarGridItem("Stream", pending) { OnSelected = _ => Task.CompletedTask }] };
        var list = new CarListTemplate("Gallery") { Sections = [new CarSection([new CarListImageRowItem("Photos", [pending])])] };

        var gridItem = Items<GridItem>(Check.Is<GridTemplate>(f.Render(grid).Template).SingleList!)[0];
        Check.Is<SectionedItemTemplate>(f.Render(list).Template);
        Check.True(gridItem.IsLoading);
        Check.Null(gridItem.OnClickDelegate);

        gate.SetResult();
        // Two sizes = two images and two rebuilds, so the first Rerender does not mean the grid image is there yet
        await Check.Eventually(() => Items<GridItem>(Check.Is<GridTemplate>(f.Render(grid).Template).SingleList!)[0].Image is not null, "grid image after load");
        var loaded = Items<GridItem>(Check.Is<GridTemplate>(f.Render(grid).Template).SingleList!)[0];
        Check.NotNull(loaded.OnClickDelegate);
    }

    // I5 (review): ActionStrip on API < 7 hosts accepts at most one action with a title; an icon still loading gets no
    // placeholder title
    private static Task Legacy_action_strip_keeps_one_titled_action()
    {
        var f = new Fixture(apiLevel: 6);
        var gate = new TaskCompletionSource<Stream>();
        var list = new CarListTemplate("List")
        {
            TrailingBarButtons = [new CarBarButton("Filter"), new CarBarButton("Sort"), new CarBarButton(CarImage.FromStream(_ => gate.Task))],
        };

        var template = Check.Is<ListTemplate>(f.Render(list, new CarAppScreenState()).Template);

#pragma warning disable CS0618 // ActionStrip: the path of hosts with API < 7
        var actions = template.ActionStrip!.Actions!.Select(a => ((IJavaObject)a).JavaCast<NativeAction>()!).ToList();
#pragma warning restore CS0618
        Check.Equal(1, actions.Count(a => a.Title is not null));
        return Task.CompletedTask;
    }

    private static async Task Alert_renders_with_icon_and_closes_before_handler()
    {
        var f = new Fixture(apiLevel: 8);
        var order = new List<string>();
        var alert = new CarAlertTemplate("Reserve?")
        {
            Subtitle = "Downtown Parking",
            Icon = CarImage.FromBytes(_png),
            Actions = [new CarAction("Yes", _ => { order.Add("handler"); return Task.CompletedTask; }) { Style = CarActionStyle.Primary }, new CarAction("No")],
        };

        var native = AlertTemplateRenderer.Render(alert, f.Context, 7, TimeSpan.FromSeconds(10), () =>
        {
            order.Add("close");
            return Task.CompletedTask;
        });
        Check.Equal(7, native.Id);
        Check.Equal("Reserve?", Text(native.Title));
        Check.Equal("Downtown Parking", Text(native.Subtitle));
        Check.NotNull(native.Icon);
        Check.Equal(2, native.Actions!.Count);
        Check.Equal(10000L, native.DurationMillis);

        ((IJavaObject)native.Actions![0]).JavaCast<NativeAction>()!.OnClickDelegate!.SendClick(new Done());
        await Check.Eventually(() => order.Count == 2, "alert action");
        Check.Equal("close", order[0]);
        Check.Equal("handler", order[1]);
    }

    private static Task Header_and_action_strip_by_api_level()
    {
        CarBarButton[] buttons = [new CarBarButton("A"), new CarBarButton(CarImage.FromBytes(_png)), new CarBarButton("C")];
        var list = new CarListTemplate("List") { TrailingBarButtons = [.. buttons] };
        var old = new Fixture(apiLevel: 6);
        var current = new Fixture(apiLevel: 7);

        var legacy = Check.Is<ListTemplate>(old.Render(list, new CarAppScreenState()).Template);
        var modern = Check.Is<ListTemplate>(current.Render(list, new CarAppScreenState()).Template);

#pragma warning disable CS0618 // ActionStrip: the path of hosts with API < 7
        Check.Equal(2, legacy.ActionStrip!.Actions!.Count);
        Check.Equal(NativeAction.TypeBack, legacy.HeaderAction!.Type);
#pragma warning restore CS0618
        Check.Equal(2, modern.Header!.EndHeaderActions!.Count);
        Check.Equal(NativeAction.TypeBack, modern.Header.StartHeaderAction!.Type);
        return Task.CompletedTask;
    }

    // Review Focus 4 on native objects: detail text → refresh, new row → new step
    private static Task Detail_change_shape_is_refresh_row_change_is_not()
    {
        var f = new Fixture(apiLevel: 7);
        var a = new CarListItem("A") { Detail = "1 free" };
        var list = new CarListTemplate("Parking lots") { Sections = [new CarSection([a, new CarListItem("B")])] };

        var before = f.Render(list);
        var detail = f.Render(list with { Sections = [new CarSection([a with { Detail = "5 free" }, new CarListItem("B")])] });
        var added = f.Render(list with { Sections = [new CarSection([a, new CarListItem("B"), new CarListItem("C")])] });
        var renamed = f.Render(list with { Title = "Other" });

        Check.Equal(Check.Is<ListTemplate>(before.Template).SingleList!.Items!.Count, before.Shape.Sections[0].Rows.Count);
        Check.Equal("5 free", Text(Items<Row>(Check.Is<ListTemplate>(detail.Template).SingleList!)[0].Texts![0]));
        Check.True(CarAppRefreshRules.IsRefresh(before.Shape, detail.Shape));
        Check.True(!CarAppRefreshRules.IsRefresh(before.Shape, added.Shape));
        Check.True(!CarAppRefreshRules.IsRefresh(before.Shape, renamed.Shape));
        return Task.CompletedTask;
    }

    private static Task Images_from_resource_bytes_asset_and_symbol()
    {
        var app = Android.App.Application.Context;
        var images = new CarAppImages(app);

        var file = Check.NotNull(images.FromFile("carkit_test.png", CarAppImages.SmallPixels));
        var appSymbol = Check.NotNull(images.FromSymbol(new CarSymbolImage(null, "ic_carkit_test"), CarAppImages.SmallPixels));
        // A framework icon (android.R.drawable) whose name is not among the app's drawables: unsupported
        var systemName = _systemIcons.First(name => app.Resources!.GetIdentifier(name, "drawable", app.PackageName) == 0);
        var bytes = Check.NotNull(images.FromBytes(_png, CarAppImages.SmallPixels));
        var asset = Check.NotNull(images.FromFile("carkit_asset.png", CarAppImages.SmallPixels));
        var tinted = images.Tint(bytes, new CarColor(255, 0, 0));

        Check.Equal(IconCompat.TypeResource, file.Icon!.Type);
        Check.Equal(app.PackageName, file.Icon.ResPackage);
        Check.Equal(IconCompat.TypeResource, appSymbol.Icon!.Type);
        Check.Null(images.FromSymbol(new CarSymbolImage(null, systemName), CarAppImages.SmallPixels));
        Check.Equal(IconCompat.TypeBitmap, bytes.Icon!.Type);
        Check.Equal(IconCompat.TypeBitmap, asset.Icon!.Type);
        Check.NotNull(tinted.Tint);
        Check.Null(images.FromSymbol(new CarSymbolImage("car.fill", null), CarAppImages.SmallPixels));
        Check.Null(images.FromFile("missing.png", CarAppImages.SmallPixels));
        return Task.CompletedTask;
    }

    private static NativeAction Action(Java.Lang.Object action) => ((IJavaObject)action).JavaCast<NativeAction>()!;

    private static T Method<T>(SignInTemplate template) where T : Java.Lang.Object => ((IJavaObject)template.SignInMethod!).JavaCast<T>()!;

    // Stage 4: SignInTemplate: PIN, QR, provider; content and provider actions with ParkedOnlyOnClickListener, bar →
    // ActionStrip
    private static async Task Sign_in_renders_pin_qr_and_provider()
    {
        var f = new Fixture(apiLevel: 8);
        var signedIn = false;
        var pin = new AndroidAutoSignInTemplate("Sign in", new AndroidAutoPinSignIn("4711"))
        {
            Instructions = "Enter the code at carkit.example",
            AdditionalText = "Terms of service",
            Actions = [new CarAction("Help")],
            TrailingBarButtons = [new CarBarButton("Other")],
        };

        var rendered = f.Render(pin);
        var template = Check.Is<SignInTemplate>(rendered.Template);

        Check.Equal("Sign in", Text(template.Title));
        Check.Equal("Enter the code at carkit.example", Text(template.Instructions));
        Check.Equal("Terms of service", Text(template.AdditionalText));
        Check.Equal(NativeAction.TypeAppIcon, template.HeaderAction!.Type);
        Check.Equal("4711", Text(Method<PinSignInMethod>(template).PinCode));
        Check.True(Action(template.Actions![0]).OnClickDelegate!.IsParkedOnly);
        Check.Equal(1, template.ActionStrip!.Actions!.Count);
        Check.True(!template.IsLoading);
        Check.Equal(CarAppTemplateTypes.SignIn, rendered.Shape.Type);
        Check.Equal("Pin", rendered.Shape.Message);
        Check.True(Check.Is<SignInTemplate>(f.Render(pin with { IsLoading = true }).Template).IsLoading);

        var qr = Check.Is<SignInTemplate>(f.Render(new AndroidAutoSignInTemplate("QR", new AndroidAutoQrCodeSignIn(new Uri("https://carkit.example/tv?code=4711")))).Template);
        Check.Equal("https://carkit.example/tv?code=4711", Method<QRCodeSignInMethod>(qr).Uri!.ToString());

        var google = new CarAction("Sign in with Google", _ =>
        {
            signedIn = true;
            return Task.CompletedTask;
        })
        {
            Image = CarImage.FromBytes(_png),
        };
        var provider = Check.Is<SignInTemplate>(f.Render(new AndroidAutoSignInTemplate("Google", new AndroidAutoProviderSignIn(google)), new CarAppScreenState()).Template);
        var button = Method<ProviderSignInMethod>(provider).Action!;
        Check.Equal(NativeAction.TypeBack, provider.HeaderAction!.Type);
        Check.Equal("Sign in with Google", Text(button.Title));
        Check.NotNull(button.Icon);
        Check.True(button.OnClickDelegate!.IsParkedOnly);
        button.OnClickDelegate.SendClick(new Done());
        await Check.Eventually(() => signedIn, "provider handler");
    }

    // Review Focus 4 (Stage 4): the app's response (UpdateAsync with ErrorMessage) rebuilds the template, and the text from
    // the new callback goes to the handler of the new model
    private static async Task Input_sign_in_submits_text_to_the_current_handler()
    {
        var f = new Fixture();
        var submitted = new List<string>();
        AndroidAutoSignInTemplate Password(string version) => new("Password", new AndroidAutoInputSignIn
        {
            Hint = "Account password",
            DefaultValue = "anna",
            InputType = AndroidAutoInputType.Password,
            KeyboardType = AndroidAutoKeyboardType.Email,
            ErrorMessage = version == "v2" ? (CarKitText)"Wrong password" : null,
            OnInputSubmitted = context =>
            {
                submitted.Add($"{version}:{context.Text}:{context.Element.Id}");
                return Task.CompletedTask;
            },
        })
        {
            Id = "password",
        };

        var first = Method<InputSignInMethod>(Check.Is<SignInTemplate>(f.Render(Password("v1")).Template));
        Check.Equal("Account password", Text(first.Hint));
        Check.Equal("anna", Text(first.DefaultValue));
        Check.Equal(InputSignInMethod.InputTypePassword, first.InputType);
        Check.Equal(InputSignInMethod.KeyboardEmail, first.KeyboardType);
        Check.Null(first.ErrorMessage);

        var second = Method<InputSignInMethod>(Check.Is<SignInTemplate>(f.Render(Password("v2")).Template));
        Check.Equal("Wrong password", Text(second.ErrorMessage));
        second.InputCallbackDelegate!.SendInputSubmitted("secret", new Done());

        await Check.Eventually(() => submitted.Count == 1, "input handler");
        Check.Equal("v2:secret:password", submitted[0]);
    }

    // Stage 4: the hook gets the builder right before Build(); a list hook for ListTemplate.Builder or
    // SectionedItemTemplate.Builder works only with the builder the connection uses
    private static Task Hooks_configure_builders_before_build()
    {
        var api7 = new Fixture(apiLevel: 7);
        var api8 = new Fixture(apiLevel: 8, sectioned: true);
        var runs = new List<string>();
        // ACTIONS_CONSTRAINTS_FAB: icon and background color required (without them AddAction throws in the hook →
        // ICarErrorHandler)
        NativeAction Fab(Fixture f) => new NativeAction.Builder()
            .SetIcon(CarAppImages.AppIcon(Android.App.Application.Context))!
            .SetBackgroundColor(AndroidX.Car.App.Model.CarColor.Blue!)!
            .SetOnClickListener(f.Context.Click(new CarAction("Search"), null))!
            .Build()!;
        var list = new CarListTemplate("Parking lots") { Sections = [new CarSection([new CarListItem("Downtown")])] }
            .ConfigureAndroidAuto(b =>
            {
                runs.Add("list");
                b.AddAction(Fab(api7));
            })
            .ConfigureAndroidAutoSectioned(b =>
            {
                runs.Add("sectioned");
                b.AddAction(Fab(api8));
            });

        Check.Equal(1, Check.Is<ListTemplate>(api7.Render(list).Template).Actions!.Count);
        Check.Equal(1, Check.Is<SectionedItemTemplate>(api8.Render(list).Template).Actions!.Count);
        Check.Equal("list,sectioned", string.Join(',', runs));

        var message = new CarMessageTemplate("Content").ConfigureAndroidAuto(b => b.SetDebugMessage("from hook"));
        Check.Equal("from hook", Text(Check.Is<MessageTemplate>(api7.Render(message).Template).DebugMessage));

        var signIn = new AndroidAutoSignInTemplate("Sign in", new AndroidAutoPinSignIn("1234")).ConfigureAndroidAuto(b => b.SetAdditionalText("from hook"));
        Check.Equal("from hook", Text(Check.Is<SignInTemplate>(api7.Render(signIn).Template).AdditionalText));

        var alert = new CarAlertTemplate("Attention").ConfigureAndroidAuto(b => b.SetSubtitle(NativeCarText.Create("from hook")!));
        Check.Equal("from hook", Text(AlertTemplateRenderer.Render(alert, api7.Context, 1, TimeSpan.FromSeconds(10), () => Task.CompletedTask).Subtitle));

        // The tab content is built by the same renderer: the child's hook works too
        var tabs = new CarTabTemplate([list with { TabInfo = new CarTabInfo("List") }, new CarGridTemplate("Grid") { TabInfo = new CarTabInfo("Grid") }]);
        var content = Check.Is<TabTemplate>(api7.Render(tabs).Template).TabContents!.Template!;
        Check.Equal(1, ((IJavaObject)content).JavaCast<ListTemplate>()!.Actions!.Count);
        Check.Equal(0, api7.Test.Errors.Count + api8.Test.Errors.Count);
        return Task.CompletedTask;
    }

    // Review Focus 3 (Stage 4): a hook exception → ICarErrorHandler, the next hook and Build() still run
    private static Task Throwing_hook_is_reported_and_rendering_continues()
    {
        var f = new Fixture(apiLevel: 7);
        var message = new CarMessageTemplate("Content")
            .ConfigureAndroidAuto(_ => throw new InvalidOperationException("hook"))
            .ConfigureAndroidAuto(b => b.SetDebugMessage("next"));

        var template = Check.Is<MessageTemplate>(f.Render(message).Template);

        Check.Equal("Content", Text(template.Message));
        Check.Equal("next", Text(template.DebugMessage));
        Check.Equal("hook", Check.Is<InvalidOperationException>(f.Test.Errors.Single()).Message);
        return Task.CompletedTask;
    }

    // I2 (review): a list hook for ListTemplate.Builder on a host with SectionedItemTemplate (API 8) does not work; CarKit
    // warns once per template and builder instead of staying silent; a matching hook gives no warning
    private static Task Hooks_for_another_builder_are_reported_once()
    {
        var list = new CarListTemplate("Parking lots") { Id = "parkings", Sections = [new CarSection([new CarListItem("Downtown")])] }
            .ConfigureAndroidAuto(_ => { });
        var api8 = new Fixture(apiLevel: 8, sectioned: true);
        api8.Render(list);
        api8.Render(list);

        var warning = api8.Test.Logs.Single(log => log.Contains("ConfigureAndroidAutoSectioned", StringComparison.Ordinal));
        Check.True(warning.StartsWith("Warning", StringComparison.Ordinal));
        Check.True(warning.Contains("parkings", StringComparison.Ordinal));

        var api7 = new Fixture(apiLevel: 7);
        api7.Render(list);
        Check.Equal(0, api7.Test.Logs.Count(log => log.Contains("ConfigureAndroidAutoSectioned", StringComparison.Ordinal)));
        return Task.CompletedTask;
    }
}
