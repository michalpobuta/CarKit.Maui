using System.Globalization;
using CarKit.Maui;

namespace CarKit.Sample.Car;

// Stage 7: the flow of each category (spec: "Categories on main-package templates", "Category decisions (Stage 7)").
// The menu has only the categories from the build (CarKitCategories) that are available on the car's platform.
// Parking/EV/fuel: POI → details → confirmation; quick ordering: recent orders → details → confirmation; driving task:
// an action grid → alert; IoT: a device grid → message; weather: a pane → stations (POIs without a map) → message;
// assistant: voice states. Depth: menu (1) → flow (2) → details (3); alerts are modals (they do not consume depth).
public sealed partial class SampleScreens
{
    private const string ParkingMapId = "parking-map";
    private const string ChargersId = "chargers";
    private const string FuelId = "fuel";
    private const string OrdersId = "orders";
    private const string DrivingTasksId = "driving-tasks";
    private const string HomeId = "home";
    private const string WeatherId = "weather";
    private const string StationsId = "weather-stations";
    private const string AssistantId = "assistant";

    private static readonly TimeSpan _assistantStep = TimeSpan.FromSeconds(1.5);

    private static readonly Place[] _parkingPlaces =
    [
        new("pk-center", "City Center", "1 Main Street", new CarLocation(52.2297, 21.0122), 0.4, 42, "$6/h"),
        new("pk-station", "Railway Station", "5 Station Square", new CarLocation(52.3236, 20.9989), 6.2, 128, "first 2 h free"),
        new("pk-airport", "Airport P3", "Terminal Road", new CarLocation(52.2906, 20.9300), 7.5, 7, "$0 with a transit ticket"),
    ];

    private static readonly Place[] _chargers =
    [
        new("ev-mall", "Downtown Mall Charger", "59 Gold Street", new CarLocation(52.2300, 21.0028), 0.9, 2, "DC 150 kW · $0.62/kWh"),
        new("ev-riverside", "Riverside Charger", "33 River Avenue", new CarLocation(52.1610, 21.0710), 9.8, 4, "AC 22 kW · $0.47/kWh"),
        new("ev-hillside", "Hillside Charger", "126 Hill Street", new CarLocation(52.2510, 20.9040), 8.1, 1, "DC 50 kW · $0.55/kWh"),
    ];

    private static readonly Place[] _fuelStations =
    [
        new("fuel-west", "West Station", "50 West Road", new CarLocation(52.2333, 20.9600), 3.1, 6, "Unleaded $6.19 · Diesel $6.29"),
        new("fuel-east", "East Station", "15 Market Street", new CarLocation(52.2500, 21.0400), 2.4, 4, "Unleaded $6.09 · LPG $2.99"),
        new("fuel-south", "South Station", "300 South Avenue", new CarLocation(52.1500, 21.0200), 11.0, 8, "Unleaded $6.25 · Diesel $6.35"),
    ];

    private static readonly Order[] _orders =
    [
        new("order-burger", "Burger Bar – combo of the day", "Burger, fries, lemonade", "22 New Street", 38),
        new("order-sushi", "Sushi Go – set of 12", "12 pieces, edamame", "5 Hop Street", 54),
        new("order-coffee", "Linden Coffee House", "2× latte, croissant", "1 Rose Street", 29),
    ];

    private static readonly (string Id, string Title, string Symbol, string Done)[] _drivingTasks =
    [
        ("task-start", "Start shift", "play.circle", "Shift started at 7:00"),
        ("task-break", "Break", "cup.and.saucer", "Break logged"),
        ("task-fuel", "Refueling", "fuelpump", "Refueling logged"),
        ("task-end", "End shift", "stop.circle", "Shift ended"),
    ];

    private static readonly (string Id, string Name, string Symbol, string Android, string Result)[] _devices =
    [
        ("device-gate", "Gate", "door.garage.open", "home", "Gate is opening – it will close in 2 minutes."),
        ("device-alarm", "Alarm", "lock.shield", "lock", "Alarm armed."),
        ("device-light", "Light", "lightbulb", "light", "Garden light turned on."),
        ("device-heat", "Heating", "thermometer.medium", "info", "Heating set to 21°C."),
    ];

    private static readonly Place[] _weatherStations =
    [
        new("ws-airport", "Airport Station", "City Airport", new CarLocation(52.1657, 20.9671), 8.6, 11, "11°C · wind 18 km/h"),
        new("ws-forest", "Forest Station", "Forest Park", new CarLocation(52.2900, 20.9600), 6.9, 10, "10°C · wind 12 km/h"),
        new("ws-center", "Center Station", "Central Tower", new CarLocation(52.2319, 21.0067), 0.6, 12, "12°C · wind 9 km/h"),
    ];

    // A conversational app (CarPlay, iOS 26.4) starts with a voice interaction
    public bool StartsWithVoice => session.Platform == CarPlatform.CarPlay && categories.HasFlag(CarAppCategory.VoiceConversational);

    // The app manages the audio session (PlayAndRecord): Platforms/iOS/Car/SampleVoiceAudio.iOS.cs; on net10.0 there is
    // no implementation
    partial void BeginListening();

    partial void EndListening();

    private bool Has(CarAppCategory category) => categories.HasFlag(category);

    private IEnumerable<CarListItem> CategoryEntries()
    {
        var carPlay = session.Platform == CarPlatform.CarPlay;
        if (Has(CarAppCategory.Parking))
        {
            yield return Entry("Parking map", "POI → details → reservation", Symbol("map", "parking"), () => session.PushAsync(ParkingMap(refreshes: 0)));
        }

        if (Has(CarAppCategory.EvCharging))
        {
            yield return Entry("EV chargers", "POI → details → charging", Symbol("bolt.car", "charging"), () => session.PushAsync(Chargers()));
        }

        if (Has(CarAppCategory.Fueling))
        {
            yield return Entry("Fuel stations", "POI → prices → navigation", Symbol("fuelpump", "charging"), () => session.PushAsync(FuelStations()));
        }

        if (carPlay && Has(CarAppCategory.QuickFoodOrdering))
        {
            yield return Entry("Quick order", "Recent orders → details → confirmation", Symbol("takeoutbag.and.cup.and.straw", "list"), () => session.PushAsync(Orders()));
        }

        if (carPlay && Has(CarAppCategory.DrivingTask))
        {
            yield return Entry("Driver tasks", "A grid of simple actions", Symbol("steeringwheel", "grid"), () => session.PushAsync(DrivingTasks()));
        }

        if (carPlay && Has(CarAppCategory.VoiceConversational))
        {
            yield return Entry("Voice assistant", "Listens → thinks → speaks", Symbol("waveform", "message"), context => PresentAssistantAsync(context.CancellationToken));
        }

        if (!carPlay && Has(CarAppCategory.Iot))
        {
            yield return Entry("Home", "A device grid → message", Symbol("house", "home"), () => session.PushAsync(Home()));
        }

        if (!carPlay && Has(CarAppCategory.Weather))
        {
            yield return Entry("Weather", "Weather pane and weather stations", Symbol("cloud.sun", "weather"), () => session.PushAsync(Weather(updates: 0)));
        }
    }

    private CarTemplate? RebuildCategory(string id) => id switch
    {
        ParkingMapId => ParkingMap(refreshes: 0),
        ChargersId => Chargers(),
        FuelId => FuelStations(),
        OrdersId => Orders(),
        DrivingTasksId => DrivingTasks(),
        HomeId => Home(),
        WeatherId => Weather(updates: 0),
        StationsId => Stations(),
        _ => null,
    };

    // Parking: CarPlay: a map region change (log), Android Auto: a refresh button. New free-spot counts under the same
    // point titles, so the host counts it as a refresh (no task step)
    private CarPointOfInterestTemplate ParkingMap(int refreshes) =>
        Places(ParkingMapId, "Parking map", _parkingPlaces, Symbol("parkingsign.circle.fill", "parking"), refreshes, "Reserve", place => $"Reserve a spot at {place.Name} for 2 h?", "Reserved until 18:30") with
        {
            OnMapRegionChanged = context =>
            {
                SampleDiagnostics.Log(string.Create(CultureInfo.InvariantCulture, $"Map: {context.Region.Center.Latitude:F3}, {context.Region.Center.Longitude:F3}"));
                return Task.CompletedTask;
            },
            OnRefreshRequested = _ => session.UpdateAsync(ParkingMap(refreshes + 1)),
        };

    private CarPointOfInterestTemplate Chargers() =>
        Places(ChargersId, "EV chargers", _chargers, Symbol("bolt.car.fill", "charging"), 0, "Start charging", place => $"Start charging at {place.Name}?", "Charging started");

    private CarPointOfInterestTemplate FuelStations() =>
        Places(FuelId, "Fuel stations", _fuelStations, Symbol("fuelpump.fill", "charging"), 0, "Navigate", place => $"Navigate to {place.Name}?", "Route sent to navigation");

    // The common POI pattern: a point → (CarPlay: a card with "Details"; Android Auto: row selection = the primary
    // action) → pane → alert
    private CarPointOfInterestTemplate Places(
        string id, CarText title, Place[] places, CarImage pin, int refreshes, string action, Func<Place, string> question, string done) => new(title)
    {
        Id = id,
        Points =
        [
            .. places.Select(place => new CarPointOfInterest(place.Location, place.Name)
            {
                Id = place.Id,
                Subtitle = place.Address,
                Summary = $"free: {Math.Max(place.Free - refreshes, 0)}",
                DetailTitle = place.Name,
                DetailSubtitle = place.Address,
                DetailSummary = place.Detail,
                PinImage = pin,
                Distance = new CarDistance(place.DistanceKm, CarDistanceUnit.Kilometers),
                PrimaryAction = new CarAction("Details", _ => session.PushAsync(PlaceDetails(place, action, question(place), done))) { Id = place.Id + "-details" },
                SecondaryAction = new CarAction("Call", _ => Logged($"Call: {place.Name}")) { Id = place.Id + "-call" },
            }),
        ],
    };

    private CarInformationTemplate PlaceDetails(Place place, string action, string question, string done) => new(place.Name)
    {
        Id = "place-" + place.Id,
        Items =
        [
            new CarInformationItem("Address", place.Address),
            new CarInformationItem("Distance", string.Create(CultureInfo.CurrentCulture, $"{place.DistanceKm:0.#} km")),
            new CarInformationItem("Details", place.Detail),
        ],
        Actions = [new CarAction(action, _ => session.PresentAsync(Confirmation(question, action, done))) { Style = CarActionStyle.Primary }],
    };

    // Confirmation: the action closes the alert before the handler runs (Stage 3), so the handler shows the result
    // right away
    private CarAlertTemplate Confirmation(CarText question, string action, string done) => new(question)
    {
        Actions =
        [
            new CarAction(action, _ => session.PresentAsync(new CarAlertTemplate(done) { Actions = [new CarAction("OK") { Style = CarActionStyle.Cancel }] }))
            {
                Style = CarActionStyle.Primary,
            },
            new CarAction("Cancel") { Style = CarActionStyle.Cancel },
        ],
    };

    // Quick ordering (CarPlay): no menu browsing, only recent orders; the app pays with the saved method
    private CarListTemplate Orders() => new("Recent orders")
    {
        Id = OrdersId,
        Sections =
        [
            new CarSection(_orders.Select(order => new CarListItem(order.Name)
            {
                Id = order.Id,
                Detail = $"{order.Items} · ${order.Price}",
                Image = Symbol("takeoutbag.and.cup.and.straw", "list"),
                IsBrowsable = true,
                OnSelected = _ => session.PushAsync(OrderDetails(order)),
            })),
        ],
    };

    private CarInformationTemplate OrderDetails(Order order) => new(order.Name)
    {
        Id = "order-details-" + order.Id,
        Items =
        [
            new CarInformationItem("Order", order.Items),
            new CarInformationItem("Pickup", order.Pickup),
            new CarInformationItem("Price", $"${order.Price}"),
        ],
        Actions =
        [
            new CarAction("Reorder", _ => session.PresentAsync(Confirmation(
                new CarText([$"Reorder for ${order.Price}?", $"${order.Price}?"]), "Order", $"Pickup at 12:30: {order.Pickup}")))
            {
                Style = CarActionStyle.Primary,
            },
        ],
    };

    // Driving task (CarPlay): simple actions, each with a confirmation
    private CarGridTemplate DrivingTasks() => new("Driver tasks")
    {
        Id = DrivingTasksId,
        Items =
        [
            .. _drivingTasks.Select(task => new CarGridItem(task.Title, Symbol(task.Symbol, "grid"))
            {
                Id = task.Id,
                OnSelected = _ => session.PresentAsync(Confirmation($"{task.Title}?", "Confirm", task.Done)),
            }),
        ],
    };

    // IoT (Android Auto): a device grid, the action result as a message
    private CarGridTemplate Home() => new("Home")
    {
        Id = HomeId,
        Items =
        [
            .. _devices.Select(device => new CarGridItem(device.Name, Symbol(device.Symbol, device.Android))
            {
                Id = device.Id,
                OnSelected = _ => session.PushAsync(new CarMessageTemplate(device.Result)
                {
                    Id = device.Id + "-result",
                    Title = device.Name,
                    Icon = Symbol(device.Symbol, device.Android),
                    Actions = [new CarAction("OK", _ => session.PopAsync()) { Style = CarActionStyle.Primary }],
                }),
            }),
        ],
    };

    // Weather (Android Auto): a current weather pane (Android Auto shows at most 4 rows), stations as POIs without a map
    // (the app map: Stage 8); "Refresh" changes only the values (the same labels), so it counts as a refresh
    private CarInformationTemplate Weather(int updates) => new("Weather – Springfield")
    {
        Id = WeatherId,
        Items =
        [
            new CarInformationItem("Now", $"{12 + updates}°C, cloudy"),
            new CarInformationItem("Wind", "15 km/h, SW"),
            new CarInformationItem("Precipitation", "20%"),
            new CarInformationItem("Tomorrow", "14°C, sunny"),
        ],
        Actions =
        [
            new CarAction("Weather stations", _ => session.PushAsync(Stations())) { Style = CarActionStyle.Primary },
            new CarAction("Refresh", _ => session.UpdateAsync(Weather(updates + 1))),
        ],
    };

    private CarPointOfInterestTemplate Stations() => new("Weather stations")
    {
        Id = StationsId,
        Points =
        [
            .. _weatherStations.Select(station => new CarPointOfInterest(station.Location, station.Name)
            {
                Id = station.Id,
                Subtitle = station.Address,
                Summary = station.Detail,
                PinImage = Symbol("thermometer.medium", "weather"),
                Distance = new CarDistance(station.DistanceKm, CarDistanceUnit.Kilometers),
                OnSelected = _ => session.PushAsync(new CarMessageTemplate($"{station.Name}: {station.Detail}")
                {
                    Id = station.Id + "-reading",
                    Title = "Reading",
                    Icon = Symbol("thermometer.medium", "weather"),
                    Actions = [new CarAction("OK", _ => session.PopAsync()) { Style = CarActionStyle.Primary }],
                }),
            }),
        ],
    };

    // Assistant (CarPlay, VoiceConversational): listening animation frames from SF Symbols (available right away → an
    // animated image). Stage 9: state action buttons (iOS 26.4; on lower versions and on the 26.0 asset CarKit omits
    // them): "End" closes the modal, "Repeat" returns to listening without closing
    private CarVoiceControlTemplate Assistant(string active) => new([
        new CarVoiceControlState("listening", new CarText(["Listening…", "Listening"]))
        {
            Image = Symbol("waveform", "message"),
            AnimationImages = [Symbol("waveform.low", "message"), Symbol("waveform.mid", "message"), Symbol("waveform.high", "message")],
            AnimationDuration = TimeSpan.FromSeconds(0.9),
            Repeats = true,
            Actions = [EndAssistant("assistant-end-listening")],
        },
        new CarVoiceControlState("thinking", new CarText(["Looking for an answer…", "Searching…"])) { Image = Symbol("ellipsis.bubble", "search") },
        new CarVoiceControlState("speaking", new CarText(["Nearest free parking: City Center", "City Center"]))
        {
            Image = Symbol("speaker.wave.2.fill", "message"),
            Actions =
            [
                new CarAction("Repeat", _ => session.UpdateAsync(Assistant("listening"))) { Id = "assistant-repeat", Image = Symbol("arrow.counterclockwise", "message") },
                EndAssistant("assistant-end-speaking"),
            ],
        },
    ])
    {
        Id = AssistantId,
        Title = "Assistant",
        ActiveStateId = active,
    };

    private CarAction EndAssistant(string id) =>
        new("End", _ => session.DismissAsync()) { Id = id, Image = Symbol("xmark.circle", "message"), Style = CarActionStyle.Cancel };

    // Simulated conversation: listens → thinks → speaks → close; the microphone (PlayAndRecord) is active only while
    // listening
    public async Task PresentAssistantAsync(CancellationToken cancellationToken)
    {
        var shown = false;
        await ShowOrExplainAsync(
            async () =>
            {
                await session.PresentAsync(Assistant("listening"));
                shown = true;
            },
            new CarText(["The voice assistant requires iOS 26.4 and the VoiceConversational category", "Requires iOS 26.4"]));
        if (shown)
        {
            BeginListening();
            _ = SimulateAssistantAsync(cancellationToken);
        }
    }

    private async Task SimulateAssistantAsync(CancellationToken cancellationToken)
    {
        try
        {
            foreach (var state in new[] { "thinking", "speaking" })
            {
                await Task.Delay(_assistantStep, cancellationToken);
                if (session.Presented?.Id != AssistantId)
                {
                    return;
                }

                if (state == "thinking")
                {
                    EndListening();
                }

                await session.UpdateAsync(Assistant(state));
            }

            await Task.Delay(_assistantStep, cancellationToken);
            if (session.Presented?.Id == AssistantId)
            {
                await session.DismissAsync();
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or CarKitException or ObjectDisposedException)
        {
            SampleDiagnostics.Log($"Assistant: {ex.Message}");
        }
        finally
        {
            EndListening();
        }
    }

    private sealed record Place(string Id, string Name, string Address, CarLocation Location, double DistanceKm, int Free, string Detail);

    private sealed record Order(string Id, string Name, string Items, string Pickup, int Price);
}
