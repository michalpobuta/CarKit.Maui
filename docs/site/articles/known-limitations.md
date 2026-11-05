# Known limitations

## Not available in CarKit 0.1

| Limitation | Why | What happens |
| --- | --- | --- |
| iOS 27 APIs: MiniPlayer opt-out, voice control overlay, background images of voice states, map panels, multi-stop routing in the UI, route details, charging connectors, navigation alert actions | Need the iOS 27 SDK and Xcode 27, which CarKit 0.1 is not built with | `AllowsMiniPlayer` and `IsOverlay` are experimental (`CARKIT0001`) and have no effect beyond logging; `CarPlayVideoBrowseTemplate` (experimental too) renders as a list; voice control is already allowed in every category on iOS 27 |
| Car App Library 1.8 and 1.9 (Car App API level 9) | No .NET binding on NuGet yet | CarKit uses Car App API levels up to 8; `CarKitAndroidMinCarApiLevel` above 8 fails with `CARKIT002` |
| Calls in Android Auto | Jetpack Core-Telecom has no .NET binding, and Android Auto requires it for calling apps | `ICarCallManager.IsSupported` is `false` on Android; `CarKitAndroidCalling=true` fails with `CARKIT004` |
| Android Automotive OS as a target | `Car.App.Automotive` has no .NET binding | `CarKitEnableAutomotive=true` fails with `CARKIT004` |
| Android Auto media templates in the Play Store | Google's Early Access Program: internal and closed testing tracks only | Off by default (`CarKitAndroidTemplatedMedia`); the default `MediaLibraryService` can be published |
| Android Auto templated messaging in the Play Store | Google's labs category: internal and closed testing tracks only | Conversations in lists work in the DHU and test tracks; messaging notifications can be published everywhere |
| Unit test helpers for Car App Library | No .NET binding of `androidx.car.app:app-testing` | Use `FakeCarHost` and the DHU |

## Behavior to be aware of

- **Validation in Release** is `Warn` by default: a template that breaks a rule is logged and sent, and the platform may
  reject it. Set `<CarKitValidation>Strict</CarKitValidation>` if you prefer exceptions.
- **Android Auto task steps** are counted conservatively. Updating a screen that is not on top, or changing it after
  going back to it, can count as a step, so `Strict` mode may report `CarTemplateLimitException` earlier than the host.
  `FakeCarHost` does not count steps at all.
- **Android Auto depth** counts sign-in and long message templates, although they do not use a task step.
- **Alerts in audio apps on CarPlay** show one action: the platform limit of the audio entitlement. CarKit keeps the first
  action of the model, so put the primary action first; in a `Throw` overflow setup the alert fails validation instead.
- **Entitlement changes in incremental builds**: changing `<CarKitCategories>` does not relink a simulator build. Delete
  `bin` and `obj` to see the new category in the CarPlay window.
- **Header actions on Android Auto**: at most two, extra ones are dropped without a log; an action with a title only may be
  rejected by hosts that require icons.
- **Fallbacks and `with`**: a copy made with `with` keeps the explicit fallback of the original, so `UpdateAsync` does not
  change what a platform that shows the fallback displays; update the fallback too.
- **Images**: a failed image stays failed until the car reconnects (it is cached). The same URI or stream image used at two
  sizes in one screen is downloaded or opened twice; a stream factory must return a new stream on every call.
- **The same `CarListItem` instance in two places** of one CarPlay list is updated in place in one place only; use
  distinct items.
- **CarPlay contact without an image** shows an empty circle.
- **Points of interest**: on Android Auto the variants of `Subtitle` are lost, the list without a map uses the place list
  limit, and `SelectedPointId` is checked before the list is truncated to its limit. Each pin image that arrives later
  refreshes all points on CarPlay.
- **Route previews on Android Auto**: the host's route list limit is not validated; the host truncates long lists.
- **Media**: CarPlay's `UpNextTitle` is not cleared when a later model has none; on Android Auto with media templates, a
  header that shows the current track counts a task step when the track changes.
- **Communication**: replying from a notification after the process was restarted removes the notification instead of
  adding the reply; a notification delegate set by the app after CarKit replaces CarKit's.
- **iOS 26.4 features on older systems**: the voice control template's bar buttons are skipped silently below iOS 26.4;
  on iOS 27 with the iOS 26.0 build, `CarPlayVideoBrowseTemplate` shows as a plain list without the video header. The
  CarPlay details header reports the "replay" playback action for every action (CarPlay behavior); check it in the
  CarPlay simulator.
- **Verified without a car**: navigation, map surfaces, the Dashboard and clusters, media and messaging were tested on
  native objects, the iOS simulator and the Android emulator. The CarPlay simulator, the DHU and real cars are part of
  the release checklist, not of the automated tests.
