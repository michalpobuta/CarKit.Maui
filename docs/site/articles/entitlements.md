# Entitlements and store approval

CarKit generates the configuration that the platforms check, but only Apple and Google can grant the permission to run a
car app on real devices and in the stores. This page describes their processes as they document them; follow the
linked pages, which change over time.

## Apple CarPlay

### What needs an entitlement

| Where the app runs | What it needs |
| --- | --- |
| iOS Simulator with **I/O → External Displays → CarPlay** | The entitlement key in the build, but no approval from Apple and no provisioning profile. CarKit adds the key to simulator builds |
| iPhone with a development build, TestFlight, App Store | The entitlement granted by Apple and a provisioning profile that contains it |

Apple notes that the [Simulator](https://developer.apple.com/documentation/carplay/using-the-carplay-simulator) can't
display a CarPlay app without the necessary entitlements. Each category has its own entitlement key, and CarKit adds the
keys of the declared categories to every build (`CarKitIosEntitlements=false` turns this off):

| Category | Entitlement |
| --- | --- |
| Audio | `com.apple.developer.carplay-audio` |
| Communication | `com.apple.developer.carplay-communication` |
| Navigation | `com.apple.developer.carplay-maps` |
| Parking | `com.apple.developer.carplay-parking` |
| EV charging | `com.apple.developer.carplay-charging` |
| Fueling | `com.apple.developer.carplay-fueling` |
| Quick food ordering | `com.apple.developer.carplay-quick-ordering` |
| Driving task | `com.apple.developer.carplay-driving-task` |
| Voice-based conversation (iOS 26.4+) | `com.apple.developer.carplay-voice-based-conversation` |
| Video (iOS 27+) | `com.apple.developer.carplay-video` |
| Public safety | `com.apple.developer.carplay-public-safety` |

### Request the entitlement

Apple's [Requesting CarPlay Entitlements](https://developer.apple.com/documentation/carplay/requesting-carplay-entitlements)
describes the process:

1. Submit a request for the entitlement of your category through Apple's CarPlay contact form
   (developer.apple.com/contact/carplay) and agree to the CarPlay Entitlement Addendum. Apple reviews each app against
   its criteria; when the request is approved, Apple adds the entitlement to your developer account as a managed
   capability.
2. In **Certificates, Identifiers & Profiles**, open the App ID of the app, enable the CarPlay capability on the
   **Additional Capabilities** tab and save.
3. Create a new provisioning profile for the updated App ID and use it to sign the app.

With CarKit you skip Apple's step of writing an entitlements file: the build adds the keys of `<CarKitCategories>`. They
must match the capabilities of the App ID, otherwise signing fails. If the app has its own `Entitlements.plist`, do not add
the CarPlay keys there as well (CarKit adds them as `CustomEntitlements`); to manage them yourself, set
`<CarKitIosEntitlements>false</CarKitIosEntitlements>`.

Apple's page does not describe TestFlight; TestFlight and App Store builds are signed with a distribution provisioning
profile, so that profile must contain the capability too. CarPlay apps are reviewed against additional guidelines from the
[CarPlay App Programming Guide](https://developer.apple.com/carplay/documentation/CarPlay-App-Programming-Guide.pdf).
Apps of the Communication category also use Siri (`com.apple.developer.siri`) and an App Group, which are ordinary
capabilities of the App ID.

Request the entitlement early: without it you can develop only in the Simulator.

## Google Play

Android Auto apps need no entitlement, but they need the right manifest entries and Google's review. Google's
[Distribute Android apps for cars](https://developer.android.com/training/cars/distribute) describes the process.

### Categories and permissions

CarKit declares the category intent filters of the `CarAppService` and the permissions from `<CarKitCategories>`:

| Category | Intent filter category | Permissions |
| --- | --- | --- |
| Navigation | `androidx.car.app.category.NAVIGATION` (+ `FEATURE_CLUSTER`) | `androidx.car.app.NAVIGATION_TEMPLATES`, `androidx.car.app.ACCESS_SURFACE` |
| Parking, EV charging, fueling | `androidx.car.app.category.POI` | `androidx.car.app.MAP_TEMPLATES` (and `ACCESS_SURFACE` with an app-drawn map) |
| IoT | `androidx.car.app.category.IOT` | — |
| Weather | `androidx.car.app.category.WEATHER` | `MAP_TEMPLATES` and `ACCESS_SURFACE` with an app-drawn map |
| Communication | `androidx.car.app.category.MESSAGING` | `POST_NOTIFICATIONS` |
| Audio | Media3 `MediaLibraryService` (no `CarAppService`) | `FOREGROUND_SERVICE_MEDIA_PLAYBACK`; `androidx.car.app.MEDIA_TEMPLATES` with templated media |

Google allows `NAVIGATION_TEMPLATES` only for navigation apps
([Build navigation apps for cars](https://developer.android.com/training/cars/apps/navigation)) and `MAP_TEMPLATES` for
the map templates of point of interest apps
([Build point of interest apps for cars](https://developer.android.com/training/cars/apps/poi)). CarKit never declares
both.

### Restricted categories

Some categories can only be published to testing tracks
([supported app categories](https://developer.android.com/training/cars#supported-app-categories)):

- **Templated media apps** (Car App Library templates for media, `CarKitAndroidTemplatedMedia`) are part of Google's Early
  Access Program and can only be published to internal and closed testing tracks. The default media integration
  (`MediaLibraryService`) can use every track.
- **Templated messaging** (conversations in Car App Library templates) and **calling** are labs categories for internal
  and closed testing tracks. Messaging notifications can use every track. Calls in Android Auto are not available in
  CarKit 0.1 (`CARKIT004`).

### Quality guidelines and review

1. Meet the [car app quality guidelines](https://developer.android.com/docs/quality-guidelines/car-app-quality) for your
   category and test every item in the Desktop Head Unit. Among them: tasks complete in five screens or fewer (CarKit's
   step counter enforces this in `Strict` mode), buttons respond within two seconds, the app launches and loads content
   in ten seconds, and no automatically scrolling text or animations while driving.
2. In the Play Console, open **Advanced settings → Form factors**, add **Android Auto** and release an app bundle that
   includes the car app to a testing track.
3. The review depends on the track: internal testing has no review, closed testing has a non-blocking review (you are
   told about problems), and open testing and production are blocked until the app passes.
4. Give the reviewers what they need: test account details in **App access** when the app requires a sign-in (a point of
   interest app that makes bookings must allow one without charging funds), and a way to use a mock location if a
   navigation or point of interest app is not available in the United States.

Debug builds of a CarKit app accept every Android Auto host, so the DHU works; release builds accept only the hosts on
CarKit's list or in `car.Android.AllowedHosts` ([Categories](categories.md#host-validator-android)).
