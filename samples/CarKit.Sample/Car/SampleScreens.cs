using System.Globalization;
using CarKit.Maui;
using Microsoft.Maui.Storage;

namespace CarKit.Sample.Car;

// Stage 3 demo screens: every shared template has at least one screen here that is reachable from the menu on both
// platforms. Screen ids are fixed so that UpdateAsync hits the same template and the stack can be restored after
// reconnecting. Platform templates (Stage 4): SampleScreens.Platform.cs; escape hatch: Platforms/iOS and
// Platforms/Android (partial). Category flows (Stage 7): SampleScreens.Categories.cs. The menu has only the categories
// declared in the build.
public sealed partial class SampleScreens(ICarSession session, CarAppCategory categories)
{
    private const string ListsId = "lists";
    private const string TemplatesId = "templates";
    private const string ModalsId = "modals";
    private const string ParkingsId = "parkings";
    private const string ImagesId = "images";
    private const string ElementsId = "elements";
    private const string CounterId = "counter";
    private const string EmptyId = "empty";
    private const string GridId = "grid";
    private const string InformationId = "information";
    private const string SearchId = "search";
    private const string MessageId = "message";
    private const string LongMessageId = "long-message";

    // 16×16 PNG (CarImage.FromBytes)
    private static readonly byte[] _png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAABAAAAAQCAIAAACQkWg2AAAAFklEQVR4nGOQW2BDEmIY1TCqYfhqAADLcvoBKIOPIQAAAABJRU5ErkJggg==");

    private static readonly Parking[] _parkings =
    [
        new("City Center", "1 Main Street", 42, "$6/h"),
        new("Railway Station", "5 Station Square", 128, "first 2 h free"),
        new("Airport P3", "Terminal Road", 7, "$0 with a transit ticket"),
    ];

    private static CarImage Symbol(string ios, string android) => CarImage.FromSystemSymbol(ios, "ic_carkit_" + android);

    // Android Auto shows at least 6 rows in a list (ConstraintManager, the default from the rules), so the menu has two
    // levels. Stage 7: a "Categories" section (flows of the declared categories, without submenus: depth 3) and the
    // template demos of Stages 3-4 only with Parking (designed for its matrix and depth 5)
    public CarListTemplate Menu()
    {
        CarSection[] sections =
        [
            new CarSection(CategoryEntries()) { Id = "menu-categories", Header = "Categories" },
            new CarSection(categories.HasFlag(CarAppCategory.Parking) ? TemplateEntries() : []) { Id = "menu-templates", Header = "Template demos" },
        ];
        return new("CarKit – templates") { Id = "menu", Sections = [.. sections.Where(section => !section.Items.IsEmpty)] };
    }

    private CarListItem[] TemplateEntries() =>
    [
        Entry("Lists", "Sections, images, updates", Symbol("list.bullet", "list"), () => session.PushAsync(Lists())),
        Entry("Templates", "Grid, tabs, pane, search, messages", Symbol("square.grid.2x2", "grid"), () => session.PushAsync(Templates())),
        Entry("Modals", "Alert and action sheet", Symbol("exclamationmark.triangle", "alert"), () => session.PushAsync(Modals())),
        Entry("Platform", "Contact, sign-in, voice, fallbacks", Symbol("rectangle.on.rectangle", "document"), () => session.PushAsync(Platform())),
    ];

    // Restores a screen by Id (after reconnecting); null = a screen that is not restored
    public CarTemplate? Rebuild(string id) => id switch
    {
        ListsId => Lists(),
        TemplatesId => Templates(),
        ModalsId => Modals(),
        ParkingsId => Parkings(),
        ImagesId => Images(),
        ElementsId => Elements(wifi: true),
        CounterId => Counter(0, 1, loading: false),
        EmptyId => Empty(),
        GridId => Grid("Services"),
        InformationId => Information(CarInformationLayout.TwoColumn),
        MessageId => Message(loading: false),
        LongMessageId => LongMessage(),
        _ => RebuildPlatform(id) ?? RebuildCategory(id) ?? (Array.Find(_parkings, p => DetailsId(p) == id) is { } parking ? Details(parking) : null),
    };

    // Escape hatch (Stage 4): Platforms/iOS (rounded grid bar buttons) and Platforms/Android (a floating "Search"
    // button on the parking list); on net10.0 (sample tests) there is no implementation, so nothing changes
    static partial void ConfigurePlatform(ref CarGridTemplate grid);

    partial void ConfigurePlatform(ref CarListTemplate parkings);

    private CarListTemplate Lists() => new("Lists")
    {
        Id = ListsId,
        Sections =
        [
            new CarSection([
                Entry("Nearby parking", "List → details (push/pop)", Symbol("parkingsign.circle", "parking"), () => session.PushAsync(Parkings())),
                Entry("Images", "File, bytes, stream, URI, symbol", Symbol("photo", "info"), () => session.PushAsync(Images())),
                Entry("List elements", "Image row, playback, toggle", Symbol("list.bullet", "list"), () => session.PushAsync(Elements(wifi: true))),
                Entry("List update", "In-place UpdateAsync and loading", Symbol("arrow.clockwise", "list"), () => session.PushAsync(Counter(0, 1, loading: false))),
                Entry("Empty list", "EmptyViewTitle / EmptyViewSubtitle", Symbol("tray", "list"), () => session.PushAsync(Empty())),
            ]),
        ],
    };

    private CarListTemplate Templates() => new("Templates")
    {
        Id = TemplatesId,
        Sections =
        [
            new CarSection([
                Entry("Grid", "CarGridTemplate", Symbol("square.grid.2x2", "grid"), () => session.PushAsync(Grid("Services"))),
                Entry("Tabs", "CarTabTemplate as the root", Symbol("rectangle.split.3x1", "grid"), () => session.SetRootAsync(Tabs())),
                Entry("Information pane", "CarInformationTemplate", Symbol("info.circle", "info"), () => session.PushAsync(Information(CarInformationLayout.TwoColumn))),
                Entry("Search", "CarSearchTemplate", Symbol("magnifyingglass", "search"), PushSearchAsync),
                Entry("Message", "CarMessageTemplate", Symbol("text.bubble", "message"), () => session.PushAsync(Message(loading: false))),
                Entry("Long message", "CarLongMessageTemplate", Symbol("doc.text", "document"), () => session.PushAsync(LongMessage())),
            ]),
        ],
    };

    private CarListTemplate Modals() => new("Modals")
    {
        Id = ModalsId,
        Sections =
        [
            new CarSection([
                Entry("Alert", "The action closes the alert and opens another one", Symbol("exclamationmark.triangle", "alert"), () => session.PresentAsync(Reservation())),
                Entry("Action sheet", "CarActionSheetTemplate", Symbol("list.bullet.rectangle", "alert"), () => session.PresentAsync(ActionSheet())),
            ]),
        ],
    };

    private static CarListItem Entry(string text, string detail, CarImage image, Func<Task> open) => Entry(text, detail, image, _ => open());

    private static CarListItem Entry(string text, string detail, CarImage image, Func<CarItemContext, Task> open) => new(text)
    {
        Id = "menu-" + text,
        Detail = detail,
        Image = image,
        IsBrowsable = true,
        OnSelected = open,
    };

    private CarListTemplate Parkings()
    {
        var parkings = ParkingList();
        ConfigurePlatform(ref parkings);
        return parkings;
    }

    // Stage 9: a featured parking lot in the list details header (CarPlay iOS 26.4, net10.0-ios26.4 asset); without this
    // feature (Android Auto, 26.0 asset, older iOS) the same content as a highlighted row in a separate section
    private CarListTemplate ParkingList()
    {
        var featured = _parkings[0];
        var nearby = new CarSection(_parkings.Select(parking => new CarListItem(parking.Name)
        {
            Id = "parking-" + parking.Name,
            Detail = $"{parking.Address} · free: {parking.FreeSpots}",
            Image = Symbol("parkingsign.circle", "parking"),
            IsBrowsable = true,
            OnSelected = _ => session.PushAsync(Details(parking)),
        }))
        {
            Header = "Nearby",
        };
        var list = new CarListTemplate("Nearby parking") { Id = ParkingsId, EmptyViewTitle = "No parking lots" };
        if (session.Capabilities.Supports(CarFeature.ListDetailsHeader))
        {
            return list with
            {
                DetailsHeader = new CarListDetailsHeader(Symbol("parkingsign.circle", "parking"))
                {
                    Title = featured.Name,
                    Subtitle = $"{featured.Address} · free: {featured.FreeSpots}",
                    Body = new CarText([$"Featured: {featured.Price}, open 24 hours", featured.Price]),
                    Actions = [new CarAction("Details", _ => session.PushAsync(Details(featured))) { Id = "featured-details", Image = Symbol("info.circle", "parking") }],
                },
                Sections = [nearby],
            };
        }

        var featuredRow = new CarListItem(featured.Name)
        {
            Id = "featured-" + featured.Name,
            Detail = $"Featured: {featured.Price}",
            Image = Symbol("star.circle", "parking"),
            IsBrowsable = true,
            OnSelected = _ => session.PushAsync(Details(featured)),
        };
        return list with { Sections = [new CarSection([featuredRow]) { Header = "Featured" }, nearby] };
    }

    private static string DetailsId(Parking parking) => "details-" + parking.Name;

    // Details as an information pane: on Android Auto the fifth task template must end it (Pane, Message, LongMessage),
    // and the path Templates → Grid → Park → parking lot → details has five of them
    private CarInformationTemplate Details(Parking parking) => new(parking.Name)
    {
        Id = DetailsId(parking),
        Items =
        [
            new CarInformationItem("Address", parking.Address),
            new CarInformationItem("Free spots", parking.FreeSpots.ToString(CultureInfo.CurrentCulture)),
            new CarInformationItem("Price", parking.Price),
        ],
        Actions = [new CarAction("Back to list", _ => session.PopAsync()) { Style = CarActionStyle.Primary }],
    };

    // Every CarImage variant (file, bytes, stream, URI, symbol with a tint, dark variant)
    private static CarListTemplate Images() => new("Images")
    {
        Id = ImagesId,
        Sections =
        [
            new CarSection([
                new CarListItem("From bytes") { Id = "bytes", Detail = "CarImage.FromBytes", Image = CarImage.FromBytes(_png) },
                new CarListItem("From file") { Id = "file", Detail = "MauiImage dotnet_bot.png", Image = CarImage.FromFile("dotnet_bot.png") },
                new CarListItem("From stream") { Id = "stream", Detail = "MauiAsset parking.png", Image = CarImage.FromStream(_ => FileSystem.OpenAppPackageFileAsync("parking.png")) },
                new CarListItem("From network") { Id = "uri", Detail = "https, loaded in the background", Image = CarImage.FromUri(new Uri("https://avatars.githubusercontent.com/u/9141961?s=128")) },
                new CarListItem("Symbol with tint") { Id = "symbol", Detail = "SF Symbol / drawable", Image = Symbol("car.fill", "parking") with { Tint = new CarColor(0x51, 0x2B, 0xD4) } },
                new CarListItem("Light / dark") { Id = "dark", Detail = "Dark variant", Image = Symbol("sun.max.fill", "info").WithDarkVariant(Symbol("moon.fill", "message")) },
            ]),
        ],
    };

    // CarListImageRowItem, playback, explicit, accessories, a disabled row and a toggle (Android Auto)
    private CarListTemplate Elements(bool wifi) => new("List elements")
    {
        Id = ElementsId,
        Sections =
        [
            new CarSection([
                new CarListImageRowItem("Gallery", [CarImage.FromBytes(_png), CarImage.FromFile("dotnet_bot.png"), Symbol("car.fill", "parking")])
                {
                    Id = "gallery",
                    OnImageSelected = context => session.PresentAsync(new CarAlertTemplate($"Image {context.ImageIndex + 1} selected")
                    {
                        Actions = [new CarAction("OK") { Style = CarActionStyle.Cancel }],
                    }),
                },
                new CarListItem("Now playing") { Id = "playing", Detail = "IsPlaying, progress 40%", IsPlaying = true, PlaybackProgress = 0.4 },
                new CarListItem("Explicit track") { Id = "explicit", IsExplicitContent = true, Accessory = CarListItemAccessory.Cloud },
                new CarListItem("Disabled") { Id = "disabled", Detail = "IsEnabled = false", IsEnabled = false, OnSelected = _ => Task.CompletedTask },
                new CarListItem(wifi ? "Wi-Fi: on" : "Wi-Fi: off")
                {
                    Id = "wifi",
                    Detail = "Toggle (Android Auto)",
                    Toggle = new CarListItemToggle(wifi) { OnToggled = context => session.UpdateAsync(Elements(context.IsChecked)) },
                    OnSelected = _ => session.UpdateAsync(Elements(!wifi)),
                },
            ]),
        ],
    };

    // A detail change → in-place update (CarPlay: CPListItem setters, AA: refresh without a step);
    // a new row → a new structure (CarPlay: UpdateSections, AA: consumes a task step)
    private CarListTemplate Counter(int count, int rows, bool loading) => new("List update")
    {
        Id = CounterId,
        IsLoading = loading,
        Sections =
        [
            new CarSection([
                new CarListItem("Counter")
                {
                    Id = "count",
                    Detail = $"Selected {count} times (in-place update)",
                    OnSelected = _ => session.UpdateAsync(Counter(count + 1, rows, loading: false)),
                },
                new CarListItem("Add row") { Id = "add", Detail = "Changes the list structure", OnSelected = _ => session.UpdateAsync(Counter(count, rows + 1, loading: false)) },
                new CarListItem("Loading for 2 s")
                {
                    Id = "load",
                    Detail = "IsLoading → result",
                    OnSelected = async context =>
                    {
                        await session.UpdateAsync(Counter(count, rows, loading: true));
                        await Task.Delay(TimeSpan.FromSeconds(2), context.CancellationToken);
                        await session.UpdateAsync(Counter(count, rows, loading: false));
                    },
                },
            ]),
            new CarSection(Enumerable.Range(1, rows).Select(i => new CarListItem($"Row {i}") { Id = $"row-{i}" })) { Header = "Rows" },
        ],
    };

    private static CarListTemplate Empty() => new("Empty list")
    {
        Id = EmptyId,
        EmptyViewTitle = new CarText(["No free spots nearby", "No spots"]),
        EmptyViewSubtitle = "Try again in a few minutes",
    };

    private CarGridTemplate Grid(string title)
    {
        var grid = GridOf(title);
        ConfigurePlatform(ref grid);
        return grid;
    }

    private CarGridTemplate GridOf(string title) => new(title)
    {
        Id = GridId,
        Items =
        [
            new CarGridItem("Park", Symbol("parkingsign.circle", "parking")) { OnSelected = _ => session.PushAsync(Parkings()) },
            new CarGridItem("Reserve", Symbol("calendar", "info")) { Text = "for 2 h", OnSelected = _ => session.PresentAsync(Reservation()) },
            new CarGridItem("Change title", Symbol("pencil", "document")) { OnSelected = _ => session.UpdateAsync(Grid(title == "Services" ? "Services (changed)" : "Services")) },
            new CarGridItem("Unavailable", Symbol("xmark.circle", "alert")) { IsEnabled = false },
            new CarGridItem("From network", CarImage.FromUri(new Uri("https://avatars.githubusercontent.com/u/9141961?s=128")))
            {
                Text = "image loaded in the background",
                OnSelected = _ => session.PresentAsync(Reservation()),
            },
        ],
        TrailingBarButtons = [new CarBarButton("Back") { OnSelected = _ => session.PopAsync() }],
    };

    // CPTabBarTemplate can only be the root (the rootOnly rule), so the tabs replace the menu as the root
    private CarTabTemplate Tabs() => new([
        new CarListTemplate("Menu")
        {
            TabInfo = new CarTabInfo("Menu", Symbol("list.bullet", "list")),
            Sections = [new CarSection([new CarListItem("Back to the template menu") { IsBrowsable = true, OnSelected = _ => session.SetRootAsync(Menu()) }])],
        },
        Grid("Grid in a tab") with { Id = "tab-grid", TabInfo = new CarTabInfo("Grid", Symbol("square.grid.2x2", "grid")) { Badge = 2 }, TrailingBarButtons = [] },
        Information(CarInformationLayout.Leading) with { Id = "tab-info", TabInfo = new CarTabInfo("Information", Symbol("info.circle", "info")) },
    ])
    {
        Id = "tabs",
        OnTabSelected = context =>
        {
            SampleDiagnostics.Log($"Tab: {context.SelectedTab.TabInfo?.Title}");
            return Task.CompletedTask;
        },
    };

    // On CarPlay a layout change means replacing the template (CPInformationTemplate.Layout is constructor-only)
    private CarInformationTemplate Information(CarInformationLayout layout) => new("City Center")
    {
        Id = InformationId,
        Layout = layout,
        Items =
        [
            new CarInformationItem("Address", "1 Main Street"),
            new CarInformationItem("Free spots", "42"),
            new CarInformationItem("Price", "$6/h"),
            new CarInformationItem(null, "Open 24 hours"),
        ],
        Actions =
        [
            new CarAction("Message", _ => session.PushAsync(Message(loading: false))) { Style = CarActionStyle.Primary },
            new CarAction("Change layout", _ => session.UpdateAsync(Information(layout == CarInformationLayout.TwoColumn ? CarInformationLayout.Leading : CarInformationLayout.TwoColumn))),
        ],
    };

    // CarPlay allows CPSearchTemplate only in the Navigation category; in Parking the validator rejects the template
    private async Task PushSearchAsync()
    {
        try
        {
            await session.PushAsync(Search());
        }
        catch (CarTemplateNotAllowedException ex)
        {
            SampleDiagnostics.Log(ex.Message);
            await session.PresentAsync(new CarAlertTemplate(new CarText(["Search requires the Navigation category on CarPlay", "Requires Navigation"]))
            {
                Actions = [new CarAction("OK") { Style = CarActionStyle.Cancel }],
            });
        }
    }

    private CarSearchTemplate Search() => new()
    {
        Id = SearchId,
        Hint = "Parking name",
        OnSearchTextChanged = context => Task.FromResult<IReadOnlyList<CarListItem>>(
            [.. _parkings
                .Where(p => p.Name.Contains(context.Text, StringComparison.CurrentCultureIgnoreCase))
                .Select(p => new CarListItem(p.Name) { Detail = p.Address, OnSelected = _ => session.PushAsync(Details(p)) })]),
        OnSearchSubmitted = context =>
        {
            SampleDiagnostics.Log($"Search: {context.Text}");
            return Task.CompletedTask;
        },
    };

    private CarMessageTemplate Message(bool loading) => new("Reservation confirmed. Entry code: 4711.")
    {
        Id = MessageId,
        Title = "City Center",
        Icon = Symbol("checkmark.circle", "message"),
        IsLoading = loading,
        Actions =
        [
            new CarAction("OK", _ => session.PopAsync()) { Style = CarActionStyle.Primary },
            new CarAction("Refresh", async context =>
            {
                await session.UpdateAsync(Message(loading: true));
                await Task.Delay(TimeSpan.FromSeconds(1), context.CancellationToken);
                await session.UpdateAsync(Message(loading: false));
            }),
        ],
    };

    private CarLongMessageTemplate LongMessage() => new(
        "Parking rules",
        "1. The parking lot is open 24 hours.\n2. The fee is charged for every started hour.\n3. Keep your ticket until you leave.\n"
        + "4. The operator is liable for damage to vehicles under the general rules.\n5. While parked, Android Auto shows the full text; CarPlay uses an information pane.")
    {
        Id = LongMessageId,
        Actions = [new CarAction("I accept", _ => session.PopAsync()) { Style = CarActionStyle.Primary }],
    };

    // Selecting an action closes the alert before the handler runs (Stage 3), so the handler can show another alert
    // right away
    private CarAlertTemplate Reservation() => new(new CarText(["Reserve a spot for 2 h?", "Reserve?"]))
    {
        Subtitle = "City Center",
        Icon = Symbol("calendar", "info"),
        Actions =
        [
            new CarAction("Reserve", _ => session.PresentAsync(new CarAlertTemplate("Reserved until 18:30")
            {
                Actions = [new CarAction("OK") { Style = CarActionStyle.Cancel }],
            }))
            {
                Style = CarActionStyle.Primary,
            },
            new CarAction("Cancel") { Style = CarActionStyle.Cancel },
        ],
    };

    private CarActionSheetTemplate ActionSheet() => new("City Center", "What do you want to do?")
    {
        Actions =
        [
            new CarAction("Show details", _ => session.PushAsync(Details(_parkings[0]))),
            new CarAction("Remove from favorites") { Style = CarActionStyle.Destructive },
            new CarAction("Cancel") { Style = CarActionStyle.Cancel },
        ],
    };

    private sealed record Parking(string Name, string Address, int FreeSpots, string Price);
}
