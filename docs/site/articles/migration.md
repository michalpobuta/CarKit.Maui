# Migrating an existing app

Adding CarKit to an app that is already in the stores changes two things on iOS: the app starts to use scenes, and some
lifecycle events arrive in different places. Android needs no structural change.

## iOS: the scene manifest

CarPlay apps must declare a CarPlay scene, so CarKit generates a scene manifest (`UIApplicationSceneManifest`) with two
scene configurations: the phone scene and the CarPlay scene (plus the Dashboard and cluster scenes of navigation apps).
The phone scene uses `CarKitPhoneSceneDelegate`, a `MauiUISceneDelegate`, so MAUI keeps creating its window as before.
A MAUI app without a scene manifest uses the older app-delegate lifecycle, so this is a real change for existing apps.

What changes and what you need to do:

| Area | Without CarKit | With the scene manifest |
| --- | --- | --- |
| `Application.OnSleep`, `OnResume` | Called | Still called (verified on the iOS 26 simulator) |
| Opening URLs (custom schemes, universal links) | `iOSLifecycle.OpenUrl` (app delegate `openURL:`) | **`iOSLifecycle.SceneOpenUrl`** (scene `openURLContexts:`). Handle `SceneOpenUrl`, or both if you share code with apps without scenes |
| `Application.OnAppLinkRequestReceived` | Not called by MAUI on iOS | Not called (unchanged) |
| Multiple windows | Not enabled | `UIApplicationSupportsMultipleScenes` = `true` (the CarPlay scene is a second scene) |

```csharp
builder.ConfigureLifecycleEvents(events => events.AddiOS(ios => ios
    .SceneOpenUrl((_, contexts) =>                       // with the CarKit scene manifest
    {
        foreach (var context in contexts)
        {
            HandleUrl(new Uri(context.Url.AbsoluteString!));
        }

        return true;
    })
    .OpenUrl((_, url, _) =>                              // apps without scenes
    {
        HandleUrl(new Uri(url.AbsoluteString!));
        return true;
    })));
```

Check every app-delegate override the app relies on. Methods that UIKit moves to the scene delegate when scenes are
enabled (URL handling, user activities, foreground and background notifications of the window) must be handled through
the corresponding scene events of MAUI.

### Your own scene delegate or manifest

- Your own phone scene delegate: derive it from `MauiUISceneDelegate`, register it with `[Register("SceneDelegate")]` and
  set `<CarKitIosPhoneSceneDelegate>SceneDelegate</CarKitIosPhoneSceneDelegate>`.
- Your own scene manifest in `Info.plist`: set `<CarKitIosGenerateSceneManifest>false</CarKitIosGenerateSceneManifest>`
  and add a scene configuration with the role `CPTemplateApplicationSceneSessionRoleApplication`, the class
  `CPTemplateApplicationScene` and the delegate `CarKitSceneDelegate` (and the Dashboard and cluster roles for
  navigation). Use the configuration name `__MAUI_DEFAULT_SCENE_CONFIGURATION__`, because
  `MauiUIApplicationDelegate.GetConfiguration` returns it for every role.

### Starting without the phone UI

CarPlay can connect the car scene while the phone is locked and the phone scene was never created. CarKit makes sure the
`MauiApp` and its services exist then, but code that runs only when the phone window is created (in `App` or
`CreateWindow`) does not run. Keep what the car needs in services, not in pages.

### Other iOS settings CarKit touches

- `UIBackgroundModes`: CarKit adds `audio` for media and navigation apps unless `Info.plist` already has the key; then
  add `audio` yourself (`CARKIT006`).
- Notifications (Communication module): CarKit installs its own `UNUserNotificationCenter` delegate at startup and
  forwards notifications outside its category to the delegate that was set before. A delegate the app sets later
  replaces CarKit's, and CarKit's message actions stop working.
- App Groups (Communication module): `<CarKitIosAppGroup>` adds `com.apple.security.application-groups`. An app that
  already declares App Groups in its own entitlements must list the CarKit group there too, because the SDK does not
  merge the arrays.
- Usage descriptions: CarKit adds English defaults for `NSSiriUsageDescription` and `NSMicrophoneUsageDescription` only
  when `Info.plist` has none; provide localized texts in `Info.plist`.

## Android

- Minimum Android version: API level 23 (`SupportedOSPlatformVersion` 23.0); the `dotnet new maui` template uses 21.
- CarKit generates a `CarAppService` subclass and its manifest entries in your app assembly; you do not edit
  `AndroidManifest.xml`. An existing `CarAppService` of the app would compete with it, so remove it.
- Android Auto starts the `CarAppService` without an `Activity`; `MauiApplication.OnCreate` creates the `MauiApp`, so
  your services are available. Code in `MainActivity` does not run.
- Media apps: CarKit's Media3 service handles the media session for Android Auto; an existing `MediaBrowserService` or
  `MediaSessionService` of the app would be a second media service. Move the app's player behind `ICarMediaPlayer`
  instead.
