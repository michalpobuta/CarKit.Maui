# Your first CarPlay & Android Auto app in 15 minutes

This guide turns a new .NET MAUI app into a parking app that shows a list of car parks in CarPlay and Android Auto. You
declare the app category once in the project file, register one handler and build the car screens from C# records. CarKit
generates the CarPlay scene manifest, the entitlements and the Android Auto service for you.

> [!NOTE]
> The repository's CI applies the steps and the code on this page to a fresh `dotnet new maui` project and builds it for
> iOS and Android (`eng/quickstart/run.sh`), so they match the current API.

## Before you start

You need the tools from [Getting started](getting-started.md#requirements):

- .NET SDK 10.0.2xx with the `maui-ios` and `maui-android` workloads,
- Xcode 26.4 with an iOS simulator (macOS only),
- the Android SDK (API 36) with an emulator or a phone (Android 9 or later) that has Google Play and the Android Auto
  app, and the Android Auto Desktop Head Unit (DHU) from the SDK Manager (**SDK Tools → Android Auto Desktop Head Unit
  Emulator**).

## 1. Create the app

<!-- quickstart: command -->
```bash
dotnet new maui -n MyCarApp
cd MyCarApp
```

## 2. Target iOS and Android

CarKit supports iOS and Android, so remove Mac Catalyst and Windows. In `MyCarApp.csproj`, replace the
`<TargetFrameworks>` lines with this single line. The plain `net10.0-ios` target is enough: the app gets CarKit's
iOS 26.4 build automatically (see [Platform features](platform-features.md)).

<!-- quickstart: replace MyCarApp.csproj ^\s*<TargetFrameworks.*</TargetFrameworks>\s*$ -->
```xml
		<TargetFrameworks>net10.0-android;net10.0-ios</TargetFrameworks>
```

CarKit needs Android 6.0 (API level 23) or later. Replace the Android `SupportedOSPlatformVersion` line:

<!-- quickstart: replace MyCarApp.csproj ^\s*<SupportedOSPlatformVersion Condition=.*'android'">21\.0</SupportedOSPlatformVersion>\s*$ -->
```xml
		<SupportedOSPlatformVersion Condition="$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'android'">23.0</SupportedOSPlatformVersion>
```

CarKit 0.1 needs .NET MAUI 10.0.120 or later, while the template references the MAUI version of the installed
workload (`$(MauiVersion)`, 10.0.20 for workload set 10.0.204.1), which NuGet reports as a package downgrade (`NU1605`).
Replace the `Microsoft.Maui.Controls` reference:

<!-- quickstart: replace MyCarApp.csproj ^\s*<PackageReference Include="Microsoft\.Maui\.Controls" Version="\$\(MauiVersion\)" />\s*$ -->
```xml
		<PackageReference Include="Microsoft.Maui.Controls" Version="10.0.120" />
```

## 3. Declare the car app category

The category decides which templates the car allows and which entitlements and manifest entries CarKit generates.
Add it to `MyCarApp.csproj`, just before `</Project>`:

<!-- quickstart: insert-before MyCarApp.csproj </Project> -->
```xml
	<PropertyGroup>
		<CarKitCategories>Parking</CarKitCategories>
	</PropertyGroup>

```

## 4. Add the package

<!-- quickstart: command -->
```bash
dotnet add package CarKit.Maui --prerelease
```

`CarKit.Maui` brings `CarKit.Maui.Core` (the template model) and the MSBuild targets that generate the platform files.

## 5. Build the car screens

Create `ParkingCarAppHandler.cs` next to `MauiProgram.cs`. CarKit calls `OnConnectedAsync` when the phone connects to the
car, even if the app's phone UI is not running. The handler sets a list as the root template; selecting a row pushes an
information template with an action that presents an alert.

<!-- quickstart: file ParkingCarAppHandler.cs -->
```csharp
using CarKit.Maui;

namespace MyCarApp;

public sealed class ParkingCarAppHandler : ICarAppHandler
{
    private static readonly (string Name, string Address, int FreeSpots)[] Parkings =
    [
        ("City Center", "1 Main Street", 42),
        ("Railway Station", "5 Station Square", 7),
        ("Airport P3", "Terminal Road", 120),
    ];

    public Task OnConnectedAsync(ICarSession session, CancellationToken ct)
    {
        var rows = Parkings.Select(parking => new CarListItem(parking.Name)
        {
            Detail = $"{parking.FreeSpots} free spots",
            IsBrowsable = true,
            OnSelected = _ => session.PushAsync(Details(session, parking.Name, parking.Address, parking.FreeSpots)),
        });

        return session.SetRootAsync(new CarListTemplate("Parking nearby") { Sections = [new CarSection(rows)] });
    }

    private static CarInformationTemplate Details(ICarSession session, string name, string address, int freeSpots) => new(name)
    {
        Items = [new CarInformationItem("Address", address), new CarInformationItem("Free spots", $"{freeSpots}")],
        Actions =
        [
            new CarAction("Reserve", _ => session.PresentAsync(new CarAlertTemplate($"A spot at {name} is reserved")
            {
                Actions = [new CarAction("OK")],
            })),
        ],
    };
}
```

## 6. Register CarKit

Replace `MauiProgram.cs` with the version below. The only new lines are `using CarKit.Maui;` and `UseCarKit`, which
registers the handler as a singleton in the app's services.

<!-- quickstart: file MauiProgram.cs -->
```csharp
using CarKit.Maui;
using Microsoft.Extensions.Logging;

namespace MyCarApp;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.UseCarKit(car => car.UseHandler<ParkingCarAppHandler>())
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
```

## 7. Run it in CarPlay

Build and start the app on an iOS simulator:

<!-- quickstart: run ios -->
```bash
dotnet build -t:Run -f net10.0-ios
```

In the Simulator, choose **I/O → External Displays → CarPlay**. The CarPlay window shows **MyCarApp** on the home
screen; open it to see the list, select a car park and tap **Reserve**. You need no approval from Apple for this: the
Simulator only shows apps that declare a CarPlay entitlement, so CarKit adds the key of your category to simulator
builds, without a provisioning profile.

What CarKit generated for you: the CarPlay scene (`CPTemplateApplicationSceneSessionRoleApplication` with
`CarKitSceneDelegate`) next to the MAUI phone scene in `Info.plist`, `UIApplicationSupportsMultipleScenes`, and the
`com.apple.developer.carplay-parking` entitlement. Read [Migration](migration.md) if your app already handles URLs or
lifecycle events on iOS: the scene manifest changes where some of them arrive.

## 8. Run it in Android Auto

Build and start the app on the emulator or phone:

<!-- quickstart: run android -->
```bash
dotnet build -t:Run -f net10.0-android
```

Connect the Desktop Head Unit:

1. On the phone or emulator, open the Android Auto settings, tap **Version** ten times to enable developer mode, then
   choose **Start head unit server** from the menu.
2. Forward the port and start the DHU from the Android SDK:

   <!-- quickstart: skip -->
   ```bash
   adb forward tcp:5277 tcp:5277
   cd $ANDROID_HOME/extras/google/auto
   chmod +x ./desktop-head-unit   # macOS and Linux, the first time
   ./desktop-head-unit
   ```

3. Open **MyCarApp** in the DHU launcher.

Debug builds accept every Android Auto host, so the DHU works without extra setup. CarKit generated a
`CarAppService` with the `androidx.car.app.category.POI` category, the map template permission and
`automotive_app_desc.xml`.

## 9. Before you test on a device

- **CarPlay on an iPhone, in TestFlight and on the App Store** needs the CarPlay entitlement for your category, granted by
  Apple, and a provisioning profile that contains it.
- **Android Auto through Google Play** needs a release on a testing track and the Android Auto form factor in the Play
  Console; Google reviews the app against the car app quality guidelines.

Both processes are described in [Entitlements and store approval](entitlements.md).

## Next steps

- [Concepts](concepts.md): the session, the template stack and updates.
- [Templates](templates.md) and [Validation and limits](validation.md): what each car allows.
- [Media](media.md), [Communication](communication.md) and [Navigation](navigation.md) modules.
- [Testing](testing.md): unit tests with `FakeCarHost`, CarPlay Simulator and DHU.
