# Testing

Test car apps on three levels: the screens and flows in unit tests with `FakeCarHost`, the result on the CarPlay
simulator and the Android Auto Desktop Head Unit, and finally in a car or a head unit.

## Unit tests with FakeCarHost

`CarKit.Maui.Testing` (`net10.0`) runs an `ICarAppHandler` against a simulated host. The host applies the same validator,
rules, fallbacks and feature gate as the real renderers, records every call and lets the test act as the driver:

```csharp
[Fact]
public async Task Selecting_a_car_park_shows_its_details()
{
    using var car = new FakeCarHost(CarAppCategory.Parking, CarPlatform.AndroidAuto);
    var session = await car.ConnectAsync(new ParkingCarAppHandler());

    var list = Assert.IsType<CarListTemplate>(session.Stack[^1]);
    await car.SelectAsync(list.Sections[0].Items[0]);

    var details = Assert.IsType<CarInformationTemplate>(session.Stack[^1]);
    Assert.Equal("City Center", details.Title.Text);
}
```

What the host can simulate:

- The driver: `SelectAsync` (rows, buttons, actions, points of interest), `SelectTabAsync`, `SearchAsync`,
  `PressBackAsync`, `DismissPresentedAsync`, `ReplyAsync` and `MarkAsReadAsync` (Android Auto conversations),
  `RefreshAsync` (Android Auto POI refresh button), `ChangeMapRegionAsync` (CarPlay POI map).
- The car: `ChangeContentStyleAsync`, `DisconnectAsync`, `FailNextCall`, `CallDelay`; `IosVersion` and `CarAppApiLevel`
  for older platforms; `Features` for the CarKit build and the car ([Platform features](platform-features.md)).
- Navigation: `Navigation`, `NavigationCalls`, `SelectTripPreviewAsync`, `StartTripFromCarAsync`,
  `EndNavigationFromCarAsync`, `RequestNavigationAsync`, `EnableAutoDriveAsync`, route sharing requests.
- Inspection: `Calls`, `Errors` (exceptions that reached `ICarErrorHandler`), `Logger`.

`FakeCarHost` does not run `ConfigureCarPlay`/`ConfigureAndroidAuto` hooks and does not count Android Auto task steps;
check long flows in the DHU. Run one test per platform and category combination the app supports. A `net10.0` test
project cannot reference the MAUI app, so keep the handlers in a class library on `CarKit.Maui.Core` or link their source
files into the test project.

## CarPlay simulator

1. Run the app on an iOS simulator (`dotnet build -t:Run -f net10.0-ios`).
2. Choose **I/O → External Displays → CarPlay** in the Simulator.
3. Test the flows, the light and dark appearance, and a start without the phone UI: close the app on the phone and open
   it from the CarPlay home screen.

The Simulator needs no approval from Apple: CarKit adds the entitlement key of your categories to simulator builds, and
the CarPlay home screen shows the app. After you change `<CarKitCategories>` or `<CarKitIosEntitlements>`, delete `bin` and
`obj` of the app: an incremental build compiles the new entitlements but does not link them into the executable, so the
app stays hidden in CarPlay or keeps the limits of the old category. Apple lists what the
[Simulator cannot test](https://developer.apple.com/documentation/carplay/using-the-carplay-simulator): a locked iPhone,
Siri and audio behavior. Test those on a device. For navigation apps, `defaults write com.apple.iphonesimulator
CarPlayExtraOptions -bool YES` adds window sizes and scales to the CarPlay options.

## Android Auto Desktop Head Unit

1. Install the Desktop Head Unit with the Android SDK Manager (**SDK Tools → Android Auto Desktop Head Unit Emulator**).
2. On a phone or emulator with Google Play and Android Auto, enable the Android Auto developer settings (tap **Version**
   ten times) and choose **Start head unit server**.
3. `adb forward tcp:5277 tcp:5277`, then start `desktop-head-unit` from `$ANDROID_HOME/extras/google/auto` (`chmod +x
   ./desktop-head-unit` the first time on macOS and Linux).
4. Install a Debug build of the app (Debug builds accept every host) and open it in the DHU launcher.

The DHU can emulate a cluster display for navigation apps, and `adb shell dumpsys activity service <CarAppService>
AUTO_DRIVE` triggers auto drive.

## In the car

Before a release, test on a phone connected to a car or head unit: start the car session with the phone locked (iOS) or
without the app's `Activity` (Android), connect and disconnect during loading, switch day and night, interrupt audio
with a phone call, and drive a route with reroutes, alerts and the second screen. Device builds for CarPlay need the
entitlement ([Entitlements](entitlements.md)).
