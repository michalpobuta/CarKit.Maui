# Categories and project configuration

## Categories

An app declares its categories once, with flags of `CarAppCategory`, in the project file:

```xml
<PropertyGroup>
  <CarKitCategories>Navigation;Parking</CarKitCategories>
</PropertyGroup>
```

| `CarAppCategory` | CarPlay | Android Auto | Package |
| --- | --- | --- | --- |
| `Audio` | audio | media (`MediaLibraryService`; templated media only in Google's Early Access) | `CarKit.Maui.Media` |
| `Communication` | communication | `MESSAGING` (calls wait for a Core-Telecom binding) | `CarKit.Maui.Communication` |
| `Navigation` | maps | `NAVIGATION` | `CarKit.Maui.Navigation` |
| `Parking` | parking | `POI` | `CarKit.Maui` |
| `EvCharging` | charging | `POI` | `CarKit.Maui` |
| `Fueling` | fueling | `POI` | `CarKit.Maui` |
| `QuickFoodOrdering` | quick-ordering | — | `CarKit.Maui` |
| `DrivingTask` | driving-task | — | `CarKit.Maui` |
| `VoiceConversational` | voice-based conversation (iOS 26.4+) | — | `CarKit.Maui` |
| `Video` | video (iOS 27+, parked only) | — | `CarKit.Maui` |
| `PublicSafety` | public safety | — | `CarKit.Maui` |
| `Iot` | — | `IOT` | `CarKit.Maui` |
| `Weather` | — | `WEATHER` | `CarKit.Maui` |

A category without a counterpart on the platform being built produces the warning `CARKIT001`, because an app may
support only one of the two systems on purpose. `car.Categories` in `UseCarKit` is filled from the project file; setting
a different value fails at startup.

Typical screens of the categories that use the templates of `CarKit.Maui`:

| Category | Screens |
| --- | --- |
| Parking, EV charging, fueling | `CarPointOfInterestTemplate` → `CarInformationTemplate` → action |
| Quick food ordering | Recent orders (`CarListTemplate`) → details (`CarInformationTemplate`) → confirmation (`CarAlertTemplate`); no full menu browsing |
| Driving task | `CarGridTemplate` of simple actions → `CarAlertTemplate` |
| IoT | `CarGridTemplate` of devices → `CarMessageTemplate` with the result |
| Weather | `CarInformationTemplate` of the current weather; stations as points of interest |
| Voice-based conversation | `CarVoiceControlTemplate` with listening, thinking and speaking states; the app owns the microphone and `AVAudioSession` |
| Video | `CarPlayVideoBrowseTemplate` (experimental, iOS 27) |

## MSBuild properties

| Property | Default | Meaning |
| --- | --- | --- |
| `CarKitCategories` | — (required) | Categories separated by `;`. Missing or unknown → `CARKIT002` |
| `CarKitValidation` | `Warn` in `Release` | Validation mode at run time (`Strict`, `Warn`, `Off`) |
| `CarKitIosGenerateSceneManifest` | `true` | Generate the scene manifest; `false` if the app maintains its own ([Migration](migration.md)) |
| `CarKitIosPhoneSceneDelegate` | CarKit's `CarKitPhoneSceneDelegate` | Your own phone scene delegate (`MauiUISceneDelegate` subclass) |
| `CarKitIosEntitlements` | CarPlay keys in every build; Siri and App Group in device builds | `true` adds Siri and App Group to simulator builds too; `false` adds no entitlements |
| `CarKitIosBackgroundAudio` | `true` for `Audio` and `Navigation` with its module | `UIBackgroundModes` = `audio` |
| `CarKitIosAppGroup` | — | App Group shared with a SiriKit Intents Extension (Communication) |
| `CarKitIosSiriUsageDescription` | English default text | `NSSiriUsageDescription` when `Info.plist` has none (Communication) |
| `CarKitIosMicrophoneUsageDescription` | English default text | `NSMicrophoneUsageDescription` when `Info.plist` has none (`VoiceConversational`) |
| `CarKitIosDashboard` | `false` | CarPlay Dashboard scene (Navigation with its module, else `CARKIT009`) |
| `CarKitIosInstrumentCluster` | `false` | CarPlay instrument cluster scene, iOS 15.4 (Navigation with its module) |
| `CarKitAndroidMinCarApiLevel` | `1` | `androidx.car.app.minCarApiLevel`; 1–8, else `CARKIT002` |
| `CarKitAndroidCluster` | `false` | Android Auto cluster (`FEATURE_CLUSTER`, Navigation with its module) |
| `CarKitAndroidTemplatedMedia` | `false` | Android Auto media templates (Early Access; `MEDIA_TEMPLATES`) |
| `CarKitAndroidCalling` | `false` | `true` fails with `CARKIT004` until Core-Telecom has a .NET binding |
| `CarKitEnableAutomotive` | `false` | `true` fails with `CARKIT004`: Android Automotive OS needs `Car.App.Automotive`, which has no .NET binding |

The targets only act in app projects (iOS `OutputType=Exe`, Android `AndroidApplication=true`), so libraries can
reference CarKit without side effects.

## What the build generates

### iOS

- The scene manifest in `Info.plist` (`UIApplicationSupportsMultipleScenes` and the scene configurations):

  | Scene role | Delegate | When |
  | --- | --- | --- |
  | `UIWindowSceneSessionRoleApplication` | `CarKitPhoneSceneDelegate` or `CarKitIosPhoneSceneDelegate` | Always |
  | `CPTemplateApplicationSceneSessionRoleApplication` | `CarKitSceneDelegate` | Always |
  | `CPTemplateApplicationDashboardSceneSessionRoleApplication` | `CarKitDashboardSceneDelegate` | `CarKitIosDashboard=true` |
  | `CPTemplateApplicationInstrumentClusterSceneSessionRoleApplication` | `CarKitInstrumentClusterSceneDelegate` | `CarKitIosInstrumentCluster=true` |

- One `com.apple.developer.carplay-*` entitlement per category ([Entitlements](entitlements.md)). Device builds need the
  matching provisioning profile. Simulator builds carry the keys as simulated entitlements, because the Simulator shows
  only apps that declare them; they need no profile (CarKit sets `CodesignRequireProvisioningProfile=false`) and get an
  `application-identifier` with a team prefix, without which the CarPlay window stays empty. The entitlements also change
  the limits of the platform ([Validation and limits](validation.md#item-limits)).
- `UIBackgroundModes` = `audio` for `Audio` and for `Navigation` with its module. When the app's `Info.plist` already
  has `UIBackgroundModes`, CarKit leaves it alone (the SDK replaces arrays) and warns with `CARKIT006` if `audio` is
  missing.
- Siri and App Group entitlements and usage descriptions for the Communication module, a microphone usage description
  for `VoiceConversational`.

### Android

- A generated `CarAppService` subclass (`CarKitGeneratedAppService`) with the intent filter `androidx.car.app.CarAppService`
  and one `androidx.car.app.category.*` per category; apps that only declare `Audio` get no `CarAppService`.
- `com.google.android.gms.car.application` metadata with `automotive_app_desc.xml` (`template`, `media`,
  `notification` uses) and `androidx.car.app.minCarApiLevel`.
- Permissions per category: `androidx.car.app.NAVIGATION_TEMPLATES` for navigation, `androidx.car.app.MAP_TEMPLATES`
  for POI and weather templates with a map, `androidx.car.app.ACCESS_SURFACE` when the app draws a map, and
  `androidx.car.app.MEDIA_TEMPLATES` for templated media. `MAP_TEMPLATES` is never declared together with
  `NAVIGATION_TEMPLATES`.
- The Media3 service (`CarKitGeneratedMediaLibraryService`, foreground service type `mediaPlayback`) for `Audio` and the
  notification action receiver (`CarKitGeneratedMessageReceiver`, not exported) plus `POST_NOTIFICATIONS` for
  Communication.

### Host validator (Android)

A `CarAppService` accepts connections only from trusted hosts. Debug builds (debuggable apps) accept every host, so the
DHU works. Release builds accept Android Auto and the Android Automotive OS template host from CarKit's list; set
`car.Android.AllowedHosts` (entries `"<SHA-256 of the signing certificate>,<package>"`) to replace it.
