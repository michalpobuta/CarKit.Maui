# Manual verification (CarPlay Simulator, DHU, devices)

Some of the stage criteria need a car screen, which cannot be started automatically in CI or in an environment without Android Auto and without the Xcode menu. This document describes those steps for `samples/CarKit.Sample`. Record the results in [release-checklist.md](release-checklist.md).

What is checked automatically (Stage 2): the generated `Info.plist` (scene manifest), entitlements (on a device and, since Stage 10, the CarPlay keys in the simulator build), the merged `AndroidManifest.xml` (service, intent filter, meta-data, permissions), `res/xml/automotive_app_desc.xml` in the APK, the `RuntimeHostConfigurationOption` values, app start on the simulator and emulator, `CarAppService` start without an `Activity`, and the unit tests of hosting, the generator and the targets.

## Stage 2 – hosting and `CarListTemplate`

The sample declares `<CarKitCategories>Parking</CarKitCategories>`; the `SampleCarAppHandler` handler shows the "Nearby parking" list.

### CarPlay Simulator

1. `dotnet build samples/CarKit.Sample -f net10.0-ios -t:Run` (iPhone simulator).
2. In Simulator: **I/O → External Displays → CarPlay**.
3. Expected:
   - [ ] The CarPlay screen shows the "CarKit Sample" icon; after selecting it, the "Nearby parking" list with the sections "Nearby" and "Demo".
   - [ ] Selecting a parking lot shows a spinner in the row, then the details list; the CarPlay Back button returns to the list (session stack: 1 template).
   - [ ] "Back to list" in the details returns the same way as Back.
   - [ ] "Reserve a spot" shows an alert with the actions "Reserve" and "Cancel"; both close the alert.
   - [ ] The phone screen shows "Car: connected (CarPlay)", and after the CarPlay window is closed "Car: disconnected".
   - [ ] Changing the CarPlay appearance (light/dark) does not crash the app.
   - [ ] Open the parking details, close the CarPlay window and open it again: the app restores the stack (list + the same details), because `ICarSession.Stack` still contains the details after disconnect.
4. Start without a phone scene: close the app (app switcher), lock the simulator (**Device → Lock**), open CarPlay and select the app.
   - [ ] The list appears without unlocking the phone; after unlocking and opening the app, the phone shows "Car: connected (CarPlay)".

### Android Auto Desktop Head Unit (DHU)

1. A phone or an emulator with Google Play and the Android Auto app. In Android Auto: settings → tap "Version" ten times (developer mode) → menu → **Start head unit server**.
2. `dotnet build samples/CarKit.Sample -f net10.0-android -t:Install` (Debug: the validator lets every host in).
3. `adb forward tcp:5277 tcp:5277`, then `desktop-head-unit` from the Android SDK (`extras/google/auto`).
4. Expected:
   - [ ] The DHU launcher shows "CarKit Sample" (POI category).
   - [ ] First a loading state, then the "Nearby parking" list: on hosts with Car App API 7+ a header with the app icon, on older ones a title.
   - [ ] Selecting a parking lot adds a screen with a Back button; the host's Back returns to the list.
   - [ ] "Reserve a spot" (host with API ≥ 5) shows an alert; clicking an action or 10 s closes it (binding 1.7.0.1 does not allow setting `AlertCallback`, see `docs/specification.md`).
   - [ ] The dark/light DHU theme does not crash the app.
   - [ ] Open the parking details, disconnect the DHU and connect again: the list + the same details, Back returns to the list (the push in `OnConnectedAsync` lands above the root).
5. A session without an `Activity`: `adb shell am force-stop com.carkit.sample`, then start the app from the DHU launcher.
   - [ ] `adb shell dumpsys activity activities | grep com.carkit.sample` shows no phone activity, and `adb logcat -s CarKit.Sample` shows `CarKit.Hosting: CarKit car app service created; CarKit services resolved from MauiApp`.

### Release builds on a device

- [ ] iOS: a provisioning profile with the `com.apple.developer.carplay-parking` entitlement (on a device the key must be in the profile); without it signing fails.
- [ ] iOS Release (AOT): CarPlay connects, which means `CarKitSceneDelegate` survived trimming.
- [ ] Android Release with Android Auto from Google Play: the host passes the validator (the `carkit_allowed_hosts` list or `car.Android.AllowedHosts`); a host outside the list does not get a session.

## Stage 3 – shared templates

What is checked automatically (Stage 3): the CarPlay update logic, the refresh rules and Android Auto task steps, the image pipeline, limits read at runtime, and the sample screens on `FakeCarHost` (`tests/CarKit.Maui.Tests`); renderers of all templates on real `CP*` objects and Car App Library models (`eng/run-device-tests.sh ios` on the simulator, `eng/run-device-tests.sh android` on the emulator). What cannot be checked automatically is how the templates look on the car screen and how the host counts steps; that is below.

Sample: the menu "CarKit – templates" → "Lists", "Templates", "Modals" (Android Auto shows at least 6 list rows, so the menu has two levels).

### CarPlay Simulator

1. As in Stage 2 (`dotnet build samples/CarKit.Sample -f net10.0-ios -t:Run`, **I/O → External Displays → CarPlay**).
2. Lists:
   - [ ] "Nearby parking": icons on the rows, arrows (browsable), details and Back as in Stage 2.
   - [ ] "Images": six rows with an image; "From network" and "From stream" appear after a moment without reloading the list; "Symbol with tint" in purple; "Light / dark" changes the icon after switching the CarPlay appearance.
   - [ ] "List elements": a gallery (image row), selecting the second image shows the alert "Image 2 selected"; "Now playing" with an indicator and 40% progress; "Explicit track" with an E mark and a cloud; "Disabled" grayed out; selecting "Wi-Fi" changes the row text without leaving the screen.
   - [ ] "List update": selecting "Counter" shows a spinner, then the detail "Selected 1 times"; the list stays the same (no transition animation); "Add row" adds a row in the "Rows" section; "Loading for 2 s" on iOS 18.4+ shows the empty-list spinner and returns to the rows.
   - [ ] "Empty list": the title "No free spots nearby" (or a shorter variant) and the subtitle.
3. Templates:
   - [ ] "Grid": four buttons, "Unavailable" is disabled; "Change title" changes the title in place; the "Back" bar button on the right.
   - [ ] "Tabs": a bar with three tabs (Grid with a badge); switching tabs; "Back to the template menu" restores the menu as the root.
   - [ ] "Information pane": a two-column layout; "Change layout" swaps the pane without animation (a one-column layout); "Message" opens a message.
   - [ ] "Search" in the Parking category: the alert "Search requires the Navigation category on CarPlay". With `dotnet build samples/CarKit.Sample -f net10.0-ios -t:Run -p:CarKitCategories=Parking%3BNavigation`: the CarPlay keyboard, typing "cen" shows "City Center", selecting the result opens the details.
   - [ ] "Message": a pane with the title "City Center", the body and the actions "OK" (goes back) and "Refresh".
   - [ ] "Long message": an information pane with the full text of the rules (fallback) and the action "I accept".
4. Modals:
   - [ ] "Alert": "Reserve" closes the alert and immediately shows "Reserved until 18:30"; "Cancel" closes the alert (no `DismissAsync` in the handler).
   - [ ] "Action sheet": a sheet with three actions; each closes it, "Show details" opens the parking details.
5. Depth: Templates → Grid → "Park" → parking lot → details (5 templates) works; CarPlay does not report a stack error.

### Android Auto Desktop Head Unit (DHU)

1. As in Stage 2 (DHU with Android Auto developer mode).
2. Lists and templates as on CarPlay, with the Android Auto differences:
   - [ ] On a host with Car App API 8 the lists are `SectionedItemTemplate` (the gallery in "List elements" as an image grid, selecting an image → an alert). If the lists do not display, build the sample with `<RuntimeHostConfigurationOption Include="CarKit.Maui.Android.SectionedItemTemplate" Value="false" />` and note the host version (a risk in `docs/specification.md`).
   - [ ] "Wi-Fi" in "List elements" has a toggle; toggling it changes the row text.
   - [ ] "List update": repeated "Counter" (a refresh) does not end with the host error "too many templates"; after "Add row" the CarKit step counter grows (in Debug, exceeding the limit of 5 ends with `CarTemplateLimitException` in the log instead of a host error).
   - [ ] "Grid": images from the `ic_carkit_*` drawables; "From network" first shows a spinner, after loading the image and it reacts to selection (an alert); the "Back" button (title only) in the header; if the host does not show it, note the host version.
   - [ ] "Tabs": a header with the app icon, tabs with icons; switching a tab shows its content; "Back to the template menu" without a host error (a different root type → a new root screen).
   - [ ] "Information pane": label–value rows and two actions ("Message" highlighted).
   - [ ] "Search": the hint "Parking name", results while typing, selecting a result opens the details.
   - [ ] "Message": an icon, "Refresh" shows loading for 1 s.
   - [ ] "Long message": only while parked; the "I accept" action works while parked.
   - [ ] "Alert" (host with API ≥ 5): "Reserve" closes the alert and shows a second one; the alert disappears after 10 s.
   - [ ] "Action sheet": a message screen with actions (fallback); an action closes the screen before the handler runs ("Show details" opens the details above the list, not above the sheet), Back closes it as well.
   - [ ] Task steps: Templates → Grid → Park → parking lot → details (the fifth template is an information pane) works without the host error "too many templates".
   - [ ] Dark/light DHU theme: the "Light / dark" icon changes without a host error.

## Stage 4 – platform templates and the escape hatch

What is checked automatically (Stage 4): the `UnsupportedTemplateBehavior` matrix (both behaviors × a template with a fallback, without a fallback, a template of the other platform, a template below the minimum version, QR sign-in below Car App API 4) and the validation of voice states and contact buttons (`tests/CarKit.Maui.Core.Tests`), the contact and voice update plan, the sign-in task steps (`tests/CarKit.Maui.Tests`), the "Platform" sample screens on `FakeCarHost`; `CPContactTemplate`, `CPVoiceControlTemplate`, `SignInTemplate` (PIN, QR, password with a callback, provider) and the `ConfigureCarPlay`/`ConfigureAndroidAuto` hooks on real native objects (`eng/run-device-tests.sh ios|android`). What cannot be checked automatically is how the CarPlay and Android Auto hosts show these templates and the hook settings; that is below.

Sample: the menu "CarKit – templates" → "Platform" (Contact, Sign-in, Voice control, No fallback). Parking category: on CarPlay, contact (Communication only) and voice (in Parking since iOS 27) end with an alert containing the validator message.

### CarPlay Simulator

1. The sample category (Parking), as in Stage 3:
   - [ ] "Contact": the alert "Contact requires the Communication category on CarPlay"; "Voice control" (iOS < 27): the alert "Voice control in the Parking category requires iOS 27 on CarPlay".
   - [ ] "Sign-in" → each method: a "Sign-in" pane with the text "Sign in using the CarKit Sample app on your phone" and the action "Back" (an explicit fallback, CarPlay has no sign-in screen).
   - [ ] "No fallback": the alert "AndroidAutoSignIn does not work on CarPlay" (`CarTemplateNotSupportedException`).
   - [ ] Escape hatch: "Templates" → "Grid": the "Back" bar button has a rounded background (`CPBarButtonStyle.Rounded`), also after "Change title".
2. Full screens: `dotnet build samples/CarKit.Sample -f net10.0-ios -t:Run -p:CarKitCategories=Parking%3BCommunication%3BNavigation`, **I/O → External Displays → CarPlay**:
   - [ ] "Contact": a card "City Center Parking Office" with a subtitle, the text "Open 7:00–22:00", an icon, and buttons for call, message and directions; "Directions" opens the parking details, the call writes "Call: …" to the "Car event" field on the phone; the message launches Siri (without the Intents Extension, Siri will say the app does not handle messages; Stage 6).
   - [ ] "Voice control": a voice modal "Listening…" with a microphone icon, after 2 s "Looking for a free spot…", then "City Center: 42 free spots", and after another 2 s the modal disappears without flicker between states (the state is switched in place).
   - [ ] Closing the voice modal by the system (the CarPlay home button and returning to the app) does not leave the modal in the session: the next "Voice control" works.

### Android Auto Desktop Head Unit (DHU)

1. As in Stage 3 (Debug, DHU in developer mode):
   - [ ] "Contact": a "City Center Parking Office" pane (phone, hours) with the action "Directions", an explicit contact fallback.
   - [ ] "Sign-in" → "PIN code": the code "4711-08" with instructions and the action "Done" (the message "Signed in as Alex."); "QR code" (host with API ≥ 4): a QR code with an address; "Password": a password field with a hint, "wrong" → the same screen with the error "Wrong password…", "carkit" → "Signed in as Alex."; "Google": a "Sign in with Google" button with an icon.
   - [ ] Sign-in only while parked: in DHU drive mode (`drive` in the DHU console / UX restrictions) the sign-in screen content is hidden, and the actions and the provider button do not work; they return after parking.
   - [ ] Task steps: Platform → Sign-in → Password → several wrong passwords → "carkit" → a message, without the host error "too many templates" (sign-in does not count toward the limit).
   - [ ] "Voice control": a message screen "Parking assistant" with a body and a state icon, changing every 2 s (each change is a new task template), then closed; Back during it closes it as well.
   - [ ] "No fallback": the alert "CarPlayContact does not work on AndroidAuto".
   - [ ] Escape hatch: "Lists" → "Nearby parking" has a floating button with a magnifier (host API ≥ 6; `ConfigureAndroidAutoSectioned` on API 8, `ConfigureAndroidAuto` below); clicking it opens search. If the host does not show it, note the host version.

## Stage 5 – media

What is checked automatically (Stage 5): the player state and commands, the position clock, the interruption policy (call, focus, output disconnect), the Now Playing buttons, paging and search, mapping to Media3 and `MPNowPlayingInfo`, auto-browse on `FakeCarHost` and the sample library (`tests/CarKit.Maui.Tests`); the generated media service, foreground permissions, `uses media`, the audio background mode, `CARKIT005` and the AA media templates (`tests/CarKit.Maui.Build.Tests`); on the emulator a `MediaBrowser` connected to the generated `CarKitGeneratedMediaLibraryService` the way Android Auto does it (root, children with paging, content style, search, transport commands, session buttons, the platform controller with custom actions) and a competing `AudioFocusRequest` (pause and resume); on the iOS simulator `MPNowPlayingInfoCenter`, `MPRemoteCommandCenter` handlers, `AVAudioSession` notifications (an interruption with `ShouldResume`, `OldDeviceUnavailable`), auto-browse and `CPNowPlayingTemplate` on real objects (`eng/run-device-tests.sh ios|android`). What cannot be checked automatically is how CarPlay and Android Auto show the library and Now Playing, or a real phone call; that is below.

Sample: `samples/CarKit.Sample.Media` (Audio category, without `UseHandler`): a library of Albums / Genres → artists → albums → tracks / Playlists ("45 tones" for paging, "Favorites"), Now Playing with shuffle, repeat, rate (1×, 1.25×, 1.5×, 2×) and "Like", the sound from the bundled WAV tones. The phone screen has the same commands and a position log (`SampleMedia: Playing … at n s`).

### CarPlay Simulator

`dotnet build samples/CarKit.Sample.Media -f net10.0-ios -t:Run`, **I/O → External Displays → CarPlay**:

- [ ] The app screen: tabs "Albums", "Genres", "Playlists" (auto-browse); in "Albums" three albums with covers.
- [ ] "Genres" → "Ambient" → "Tone Quartet" → "Morning" → "Coffee": a tone is audible, CarPlay shows Now Playing with the title "Coffee", the artist, the cover and progress.
- [ ] Now Playing: shuffle and repeat change state (the icon is highlighted), the rate cycles 1× → 1.25× → 1.5× → 2× → 1× (the progress bar speeds up), "Like" (the heart) writes `liked=True` to the log; "Up Next" shows the album queue, selecting a track returns to Now Playing.
- [ ] The "Now Playing" button in the CarPlay bar (added by the system) opens the shared Now Playing; Back from it and further browsing work (the system Now Playing does not mix with the CarKit stack).
- [ ] "Playlists" → "45 tones": the list ends with a "More" row that adds further tracks (up to the CarPlay list limit).
- [ ] Sports mode (iOS 18.4+): not in the sample code; checked by a device test (`ConfigureCarPlay` sets `CPNowPlayingModeSports`); if the app uses it, Now Playing shows the teams and the score.
- [ ] Interruption: on an iPhone with CarPlay (device) an incoming call during playback pauses the sound; after hanging up playback resumes by itself (iOS sends `ShouldResume`). Declining the call resumes it too.
- [ ] Unplugging headphones/AirPlay (device) pauses playback without resuming.
- [ ] Locked phone: CarPlay start and playback work (UIBackgroundModes `audio`).

### Android Auto Desktop Head Unit (DHU)

An app with Audio only has no `CarAppService`; Android Auto connects to `CarKitGeneratedMediaLibraryService` (Media3).

- [ ] The "CarKit Media" app is on the DHU audio app list (Debug, Android Auto developer mode: unknown sources).
- [ ] Library: "Albums" as a grid (grid style), "Genres" → "Ambient" → "Tone Quartet" → "Morning" → tracks as a list; covers visible (`android.resource://`).
- [ ] Search (magnifier icon / voice "search neon"): the result "Neon"; selecting it plays.
- [ ] Playing from the list: a tone is audible, the playback screen with the title, the artist and progress; play/pause, next/previous, seeking with the bar.
- [ ] Custom actions: shuffle (the icon changes on/off), repeat (off → all → one), rate, "like"; the changes are also visible on the phone screen.
- [ ] Queue (the list icon in the player): the four tracks of the album; selecting one switches the track.
- [ ] A media notification on the phone during playback (a `mediaPlayback` foreground service; Android 14+ without `SecurityException` in logcat).
- [ ] Interruption: an incoming call on the phone (device) or a navigation prompt (ducking since Android 8 lowers the volume instead of pausing); after the call playback resumes; disconnecting Bluetooth headphones pauses without resuming.
- [ ] Media templates (Early Access, optional): a build with `-p:CarKitAndroidTemplatedMedia=true` and `media.UseTemplatedMediaOnAndroid` from the build; a host with Car App API 8 shows `MediaPlaybackTemplate` after selecting a track in the template screens; note the host version if the template is rejected.

## Stage 6 – communication

What is checked automatically (Stage 6): the conversation model, rules and validation (only the Communication category, messages required, no `OnSelected`), the Intents Extension store (`CarConversationStore`: atomic write, request queue, parallel drain) and `FakeCarHost.ReplyAsync`/`MarkAsReadAsync` (`tests/CarKit.Maui.Core.Tests`); the module options, notification content, the reply router, the conversation state machine, the sample screens and the extension logic (`tests/CarKit.Maui.Tests`); MESSAGING without CALLING, `CARKIT004` (`CarKitAndroidCalling`), `CARKIT007`, `uses notification`, the receiver, `POST_NOTIFICATIONS`, the Siri and App Group entitlements, `NSSiriUsageDescription` (`tests/CarKit.Maui.Build.Tests`); on the emulator a `ConversationItem` from the real renderer and a callback through the host path (`ConversationCallbackDelegate` → AIDL stub → `ConversationCallback` proxy), the `Row` fallback below API 7, a `MessagingStyle` notification from `NotificationManager` (people, messages, semantic actions, `RemoteInput`, `ShowsUserInterface = false`), a reply through a `PendingIntent` with a `RemoteInput` result and a read mark → `ICarMessageHandler`, separate actions of two conversations; on the iOS simulator `CPMessageListItem` (creation and in-place update), the `carkit.message` category with `AllowInCarPlay`, the sample Intents Extension handler (read, reply, mark through the store → `ICarMessageHandler`) and CallKit (the simulator resets the provider and CarKit ends the call as `Failed`) — `eng/run-device-tests.sh ios|android`. What cannot be checked automatically: reading and replying by voice in Android Auto, Siri in CarPlay (needs a signed build with the Siri and App Group entitlements), the contact in CarPlay Simulator, and a CallKit call in the car; that is below.

Sample: `samples/CarKit.Sample.Communication` (Communication category, `com.carkit.sample.communication`) with the Intents Extension `samples/CarKit.Sample.Communication.Intents`. Car: the "Messages" list, with the section "Conversations" (Anna Brown, Project team) and "Contacts" (Anna, Peter, Olivia → a contact with Call and Message buttons; on Android Auto a pane with the number). Phone: "Allow notifications", "Incoming message from Anna", "Incoming message in the team chat", "Incoming call from Olivia", "Call Anna", "End call" and the event log (replies from the car, notifications and Siri).

### CarPlay Simulator and device

`dotnet build samples/CarKit.Sample.Communication -f net10.0-ios -t:Run`, **I/O → External Displays → CarPlay**. Siri, the Intents Extension and CallKit work only on a device with a signed build (the CarPlay Communication, Siri and App Group `group.com.carkit.sample.communication` entitlements in the profile of the app and the extension):

- [ ] "Allow notifications" on a device (a signed build with the App Group): the system asks about notifications (from CarPlay) and then about Siri; without consent, selecting a conversation in CarPlay does not reach the Intents Extension.
- [ ] The app screen: the "Messages" list with the sections "Conversations" and "Contacts"; "Anna Brown" with an unread dot and a time, "Project team" with a mute icon.
- [ ] "Incoming message from Anna" on the phone: Anna's row gets a dot and the new text without rebuilding the list; the notification appears in CarPlay (the `carkit.message` category with `AllowInCarPlay`), Siri reads it.
- [ ] Selecting a conversation row (device): Siri reads the unread messages (Intents Extension, `INSearchForMessagesIntent`) and offers a reply; the dictated reply reaches the phone log as "reply to anna: …" and the conversation (`INSendMessageIntent` → App Group store → `ICarMessageHandler`); after reading, the messages are read (`INSetMessageAttributeIntent`).
- [ ] "Contacts" → "Anna Brown": a `CPContactTemplate` with the name, the number and the Call and Message buttons.
- [ ] Call: a CallKit call "Anna Brown" in the CarPlay Phone app (dialing, connected after 2 s), ending from the car → the log "call with Anna Brown ended".
- [ ] Message: Siri asks for the text and sends it through the Intents Extension (the log "reply to anna: …").
- [ ] A Siri reply while the app is suspended or closed: it reaches `ICarMessageHandler` only after the app is launched (a limitation of the App Group bridge; the messenger sends the reply from the extension); note the delivery time.
- [ ] "Incoming call from Olivia" on the phone: CarPlay shows the incoming call; answering in the car → the log "answered Olivia Green", the state "Active"; hold and mute from the call screen change the state in the log.
- [ ] A reply from the notification on the phone (long-press → text field) and "Mark as read" → the log "reply to …" / "… marked as read".
- [ ] Locked phone: CarPlay start, the conversation list and Siri work.

### Android Auto Desktop Head Unit (DHU)

The app has a `CarAppService` with the `MESSAGING` category (without `CALLING`, calls wait for the Jetpack Core-Telecom binding) and `automotive_app_desc` with `template` and `notification`:

- [ ] The "CarKit Messages" app is on the DHU app list (Debug, Android Auto developer mode: unknown sources).
- [ ] The app screen: a `ListTemplate` (also on a host with Car App API 8) with the sections "Conversations" (`ConversationItem` conversation rows) and "Contacts".
- [ ] Selecting the conversation "Anna Brown": Android Auto reads the messages aloud, "Reply" → the dictated reply appears in the phone log and in the conversation (the `ConversationCallback` callback through the proxy), and the conversation stops being unread (`onMarkAsRead`).
- [ ] "Incoming message from Anna" on the phone: the DHU shows the notification, reads it aloud and offers a reply; a voice reply → the log "reply to anna: …", the notification on the phone receives the reply as a "Me" message; "Mark as read" (DHU after reading) removes the notification.
- [ ] "Incoming message in the team chat": a group notification with the title "Project team" and the sender "Peter Wilson".
- [ ] "Contacts" → a contact: a pane with the number (the `CarPlayContactTemplate` fallback); no calls in Android Auto (`ICarCallManager.IsSupported = false`).
- [ ] A host with Car App API < 7 (old Android Auto): conversation rows as plain rows with the last message, no replies from the list (notifications work).
- [ ] Release with R8 (`AndroidLinkTool=r8`): a reply from the conversation list works (the `-keep` rule of the `ConversationCallback` interface).

## Stage 7 – POI and the remaining categories

What is checked automatically (Stage 7): the POI model (`CarDistance`, `CarMapRegion`, voice animations), the validator (a selected point, distance on Android Auto, the 12-point CarPlay limit and the Android Auto place list, the full CarPlay matrix rows from the specification, a template outside the category and the depth of each category), `FakeCarHost.ChangeMapRegionAsync`/`RefreshAsync` (`tests/CarKit.Maui.Core.Tests`); the choice of `PlaceListMapTemplate`/a list by category, distance units, refreshes and task steps, the flows of all sample categories in Strict (`tests/CarKit.Maui.Tests`); the entitlements of each category (including the verified voice/video/public safety keys), `CARKIT001` both ways, `IOT`/`WEATHER` without map permissions, POI + Navigation only with `NAVIGATION_TEMPLATES`, `NSMicrophoneUsageDescription` (`tests/CarKit.Maui.Build.Tests`); on the iOS simulator `CPPointOfInterestTemplate` (points, `MKMapItem`, card buttons, pins including a late one, the delegate: selection and map region, in-place updates, 12 points, hooks) and an animated voice state; on the emulator `PlaceListMapTemplate` (places, markers without a row image, `DistanceSpan`, a click and a refresh through the host path, loading, hooks) and a place list without a map — `eng/run-device-tests.sh ios|android`. What cannot be checked automatically: the host map and the detail card in the car, a map region change by the driver, the refresh button in the DHU, the microphone in CarPlay, and the flows in CarPlay Simulator/DHU; that is below.

Sample: `samples/CarKit.Sample` shows a "Categories" section in the root with the flows of the declared categories. By default `Parking` ("Parking map" + the template demos); the other categories in build variants:

```bash
# CarPlay: EV, fuel, quick ordering, driving task, voice assistant (iOS 26.4)
dotnet build samples/CarKit.Sample -f net10.0-ios -t:Run -p:CarKitCategories=EvCharging%3BFueling%3BQuickFoodOrdering%3BDrivingTask%3BVoiceConversational
# Android Auto: IoT and weather
dotnet build samples/CarKit.Sample -f net10.0-android -t:Run -p:CarKitCategories=Iot%3BWeather
```

### CarPlay Simulator

**I/O → External Displays → CarPlay** (Xcode 26.4, iOS 26.4 simulator):

- [ ] Parking: "Parking map": a map with three pins (a parking icon), a list with the title, the address and the number of free spots; selecting a pin shows a card (name, address, price) with the buttons "Details" and "Call".
- [ ] Panning/zooming the map: the phone screen shows "Map: <lat>, <lon>" (`OnMapRegionChanged`).
- [ ] "Details" → information pane → "Reserve" → an alert with a question → "Reserve" → the alert "Reserved until 18:30".
- [ ] Dark theme: the pins and buttons are legible, the card is unchanged.
- [ ] Variant: "EV chargers" and "Fuel stations": the same flow (fuel: depth 3, menu → map → pane; alerts do not consume depth).
- [ ] Variant: "Quick order": "Recent orders" → details → "Reorder" → an alert with the price → "Order" → "Pickup at 12:30…".
- [ ] Variant: "Driver tasks": a grid of four actions, each with a confirmation.
- [ ] Variant with `VoiceConversational` (iOS 26.4): after connecting, the app immediately shows the assistant: "Listening…" with an animated wave, then "Looking for an answer…", "Nearest free parking…", close; the phone shows "Microphone: PlayAndRecord active" (on a device the system asks for the microphone, `NSMicrophoneUsageDescription` from the build).
- [ ] A device with the entitlement: an `ios-arm64` build of each category gets its own key (`codesign -d --entitlements - <app>`), including `com.apple.developer.carplay-voice-based-conversation`.

### Android Auto Desktop Head Unit (DHU)

- [ ] Parking: "Parking map": a `PlaceListMapTemplate` with the host map, markers with a parking icon and the rows "<distance> · <address>", "free: N"; the refresh button in the header reduces the free-spot counts without a new task step (counter: further screens still fit within 5).
- [ ] Selecting a row → the detail pane (the card's primary action) → "Reserve" → an alert; the path menu → map → pane → alert fits within the task limit.
- [ ] A host with Car App API < 5: no refresh button, the list works.
- [ ] The `Iot;Weather` variant: the app in the IoT and weather categories; "Home" → a grid of four devices → a result message → "OK"; "Weather" → a pane (4 rows) → "Refresh" (a refresh) → "Weather stations" → a place list without a map, with distances → a reading message.
- [ ] A mixed POI + Navigation app: the manifest only with `NAVIGATION_TEMPLATES` (without `MAP_TEMPLATES`), the place list still with the host map (if the host rejects `PlaceListMapTemplate` without `MAP_TEMPLATES`, report it, the Stage 7 ruling).

## Stage 8 – Navigation

What is checked automatically (Stage 8): the model and state machine of the navigation session (transitions, restoring state after connecting, ending by the car, surface errors), `CarMapTemplate` validation (the action strip on Android Auto, the map button limit), `FakeCarHost.Navigation` with a driver simulation (`tests/CarKit.Maui.Core.Tests`); gestures, surfaces, frame statistics, the voice queue with a watchdog, Android Auto template selections, refreshes and steps, the simulated sample route from preview to finish in Strict on CarPlay and Android Auto (`tests/CarKit.Maui.Tests`); `CARKIT008/009`, the Dashboard/cluster scene roles with `CPSupports…NavigationScene`, `FEATURE_CLUSTER`, surface permissions (`tests/CarKit.Maui.Build.Tests`); on the iOS simulator `CPMapTemplate` (created through `objc_msgSend`, buttons, bar, hooks, in-place update), `CPNavigationSession` without CarPlay (`StartNavigationSession`, maneuvers, iOS 17.4/18 lanes, `ResumeTrip`, `PresentNavigationAlert`), the map delegate (preview, start, cancel, pan, iOS 26 zoom, the pan panel), surfaces in a substitute window (pixels: drawing, `UIViewController`, a MAUI view), Dashboard and cluster through the module, `AVSpeechSynthesizer` with `AVAudioSession` VoicePrompt/DuckOthers and restoration; on the emulator `CarKitSurfaceCallback` on a `Surface` from an `ImageReader` (pixels: drawing without a `VirtualDisplay`, `VirtualDisplay` + `Presentation`, a MAUI view; gestures and areas), `NavigationTemplate` (RoutingInfo, steps, lanes, roundabout, estimate, bars, pan through the host path), pause and `MessageInfo`, `MapWithContentTemplate` with a route list (selection and Start through the host path), `RoutePreviewNavigationTemplate` below API 7, `Trip` for `NavigationManager`, POI with the app map, the cluster template, the `ACTION_NAVIGATE` intent, transient focus for a prompt — `eng/run-device-tests.sh ios|android`; the sample launched on the simulator and emulator (the simulation from preview to the end, log). What cannot be checked automatically: CPMapTemplate and maneuver cards in CarPlay, Dashboard and cluster from a real car/CarPlay Simulator, the Android Auto host (`NavigationManager`, `SurfaceCallback` from the host, the DHU cluster), and voice prompts through the car speakers; that is below.

Sample: `samples/CarKit.Sample.Navigation` (the `Navigation` category, Dashboard and cluster enabled):

```bash
dotnet build samples/CarKit.Sample.Navigation -f net10.0-ios -t:Run
dotnet build samples/CarKit.Sample.Navigation -f net10.0-android -t:Run
```

### CarPlay Simulator

**I/O → External Displays → CarPlay** (Xcode 26.4); in the CarPlay Simulator settings enable Dashboard and cluster ("Extra options", instrument cluster):

- [ ] The CarPlay screen: a map drawn by the app in the window (grid, route, car), map buttons (center, +, −) and "Destination" in the bar; a finger drag pans the map, a pinch (iOS 26) zooms.
- [ ] "Destination" → "Work" → a preview of two routes (`ShowTripPreviews`), route estimates, the start button "Go"; selecting the second route highlights it (the `preview` log), and the trip estimate shows the time and distance of that route (21 min, 7.1 km).
- [ ] "Go" → a maneuver card with an icon and lanes (iOS 17.4+), the maneuver and destination estimates decrease every ~2 s, voice prompts duck other audio (e.g. music from Apple Music).
- [ ] The alert "Traffic jam on the route" with the actions "Detour"/"Stay"; the pause "Calculating a detour", then the detour with new maneuvers; arrival "You have arrived" and the end of guidance.
- [ ] Ending the route with the CarPlay button while driving: the simulation stops (the log "simulation stopped"), the phone shows `Idle`.
- [ ] The app in the background (another app on the CarPlay screen): maneuver notifications; selecting a notification opens the app (the `maneuver` log).
- [ ] Dashboard: the app map in the Dashboard window, the CarPlay maneuver card during guidance.
- [ ] Cluster: the app map in the cluster window, the steering-wheel zoom buttons zoom in; navigation metadata (iOS 17.4) in the cluster.
- [ ] Phone → "CarPlay map: MapKit / drawn": the CarPlay window shows an `MKMapView` (native mode), back to the drawn map.
- [ ] CarPlay dark theme: the drawn map in dark colors (the surface `ContentStyle`); switching day/night in CarPlay Simulator during the connection redraws the map of the screen, Dashboard and cluster without reconnecting.
- [ ] Phone locked during guidance: voice prompts and maneuver cards continue (`UIBackgroundModes` = `audio` from CarKit); background position needs `location` in the app's `Info.plist`.
- [ ] Areas: with the navigation bar and the maneuver card shown, the destination name (drawn in the stable area) is not covered.

### Android Auto Desktop Head Unit (DHU)

`desktop-head-unit` with a cluster: `desktop-head-unit --config <ini with a [display] cluster>` (DHU 2.x: the `configs/dhu_cluster.ini` configuration); auto-drive: `desktop-head-unit --autodrive` or `adb shell dumpsys activity service com.carkit.sample.navigation/carkit.maui.generated.CarKitGeneratedAppService AUTO_DRIVE`:

- [ ] The app map on the surface (`SurfaceCallback`), the map bar with a pan button (PAN) and the +/−/center buttons, the "Destination" action bar; in pan mode dragging moves the map, a pinch zooms, a fling.
- [ ] "Destination" → "Work" → `MapWithContentTemplate` with a list of two routes (estimates "km · min"), route selection, "Go"; the transition to `NavigationTemplate` fits within the task limit.
- [ ] Guidance: a step card with a maneuver, lanes (a lane image) and a distance, the next step (roundabout, 3rd exit), the destination estimate; updates every ~2 s without new task steps.
- [ ] Three routes in a row (preview → "Go" → end), then "Destination" → the destination list: no template limit error (`NavigationTemplate` resets the limit) and no host error "template type" after selecting a destination (after returning, the map sends the previous template first).
- [ ] DHU night mode (`day`/`night` in the DHU console): the app map and the cluster redrawn in the new style.
- [ ] The alert "Traffic jam on the route" (10 s, actions), pause (loading), detour, arrival (`MessageInfo` "You have arrived"), end (`navigationEnded`).
- [ ] Stopping navigation from the host (e.g. another navigation app or "Stop navigation"): `onStopNavigation` → the simulation stops, voice prompts go silent.
- [ ] `--autodrive`/`AUTO_DRIVE`: after selecting a destination the route starts by itself.
- [ ] Assistant: "Navigate to City Airport" (`ACTION_NAVIGATE`, `geo:`) → a route preview to the airport.
- [ ] DHU cluster: the app map (the cluster `NavigationTemplate`) and the guidance step; no controls.
- [ ] Voice prompts with transient focus: music from another app is lowered for the duration of the prompt.
- [ ] A POI app (`Parking`) with the Navigation module and `CarScreen` content on a host with Car App API 7: a place list in `MapWithContentTemplate` with the app map; a host with API < 7: `PlaceListMapTemplate`.

### A real car or head unit

- [ ] CarPlay (an iPhone with the `carplay-maps` profile): the whole sample route, Dashboard, cluster (a car with a CarPlay cluster), prompts through the car speakers.
- [ ] Android Auto (a phone with Android Auto): the whole sample route, cluster (a car with an Android Auto cluster), prompts through the car speakers.

### Map smoothness measurement

Method: `ICarMapSurface.FrameStatistics` (the last ≤ 600 frames: mean, median, p95, max, frames > 16.7 ms and > 33.3 ms) and the `CarKit.Navigation` log every 120 frames with `nav.LogFrameTimes = true` (the sample has it enabled). What counts as frame time: custom drawing is `ICarSurfaceRenderer.Draw` with the lock and canvas publication (Android: `LockHardwareCanvas` → `UnlockCanvasAndPost`, iOS: `drawRect` of the `PlatformGraphicsView` view); a native view and MAUI on Android is `FrameMetrics.TOTAL_DURATION` of the `Presentation` window on a `VirtualDisplay` (from the intended vsync to the buffer being sent, so ~1 vsync period is normal); a native view and MAUI on iOS is the interval between `CADisplayLink` frames (a main thread stall lengthens the interval). The device tests `Frame_times_custom_and_native` (iOS) and `Frame_times_custom_and_virtual_display` (Android) measure 120 frames of custom drawing (800×480, an animated point) and 2 s of native view animation; the `INFO CARKIT-FRAMES` lines in the test log.

Results of 2026-10-30 (iPhone 17 simulator, iOS 26.0, on a Mac with Apple Silicon; emulator `emulator-5554`, API 35, arm64):

| Mode | Frames | Mean | p50 | p95 | Max | > 33.3 ms |
| --- | --- | --- | --- | --- | --- | --- |
| iOS custom drawing (`Draw` time) | 120 | 0.02 ms | 0.02 ms | 0.03 ms | 0.07 ms | 0 |
| iOS `MKMapView` (`CADisplayLink` interval) | 119 | 16.95 ms | 16.67 ms | 16.67 ms | 42.35 ms | 1 |
| Android custom drawing (lock/draw/post) | 120 | 0.37 ms | 0.35 ms | 0.63 ms | 0.76 ms | 0 |
| Android `VirtualDisplay` + `Presentation` (`TOTAL_DURATION`) | 120 | 16.26 ms | 17.07 ms | 17.75 ms | 18.45 ms | 0 |

Further runs the same day after the review (the `VirtualDisplay` path unchanged in code) gave on the emulator a `TOTAL_DURATION` mean of 28.6–42.0 ms (p95 34–146 ms) and custom drawing of 0.65–1.28 ms, with the emulator under memory pressure (2 GB RAM, ~595 MB of swap) and a loaded host; iOS unchanged (p95 16.67 ms). The emulator measurement therefore depends heavily on the host state; the measurement on a device is the binding one.

Conclusion: both modes fit within 60 Hz on the simulator/emulator on a quiet host; custom drawing costs < 1 ms of CPU per frame, and the `VirtualDisplay` adds a full HWUI pass of the window (~1 vsync). On a real device and head unit, measure again with `nav.LogFrameTimes = true` (the `CarKit map frames …` log) and add the result to [release-checklist.md](release-checklist.md).

## Stage 9 – new platform features

What is checked automatically (Stage 9): the feature gate (`CarFeature`, `ICarCapabilities.Supports`, the `features` rules, removing features before the host, the log), the models (voice action buttons, list header, playback state, sports mode, MiniPlayer and the iOS 27 overlay behind the gate, the iOS 27 video list), route segments, the detour reason and car requests on `FakeCarHost` (`tests/CarKit.Maui.Core.Tests`); the CarPlay update planner, the `ShareRoutesWithCar` option, the sample ("Featured" header or row, assistant buttons, a broadcast with sports mode, a route with a stop) (`tests/CarKit.Maui.Tests`); `_CarKitMaxCarApiLevel` (`tests/CarKit.Maui.Build.Tests`); on iOS simulators in three configurations (the 26.4 asset on iOS 26.0 and on iOS 26.4, the 26.0 asset on iOS 26.4): `CPVoiceControlState.ActionButtons` and the voice modal bar, `CPListTemplateDetailsHeader`, the `CPPlaybackConfiguration` of rows, the video list as a `CPListTemplate`, `CPNowPlayingModeSports`, `CPTrip` with `CPNavigationWaypoint`, `CPRouteSegment` on `CPNavigationSession` (`AddRouteSegments`, `CurrentSegment`, `ResumeTrip` with a reason), the map delegate (a stop request with a completion, route source, destination, response to a proposal); on the emulator the voice fallback with actions, a list without a header, `Trip` with segment destinations — `eng/run-device-tests.sh ios|android` (`IOS_ASSET=26.0` for the 26.0 asset); packages with both iOS assets and the consumer smoke test (`eng/nuget-smoke/smoke.sh`). What cannot be checked automatically: the look of the header, voice buttons and sports mode in CarPlay, route sharing (needs a car with this feature), and video (iOS 27, a car with video, CarPlay Simulator with Additional Tools for Xcode 27); that is below.

### CarPlay Simulator (iOS 26.4, Xcode 26.4)

A simulator with iOS 26.4 (e.g. "CarKit iPhone 26.4"); apps from `net10.0-ios` get the 26.4 asset:

- [ ] `samples/CarKit.Sample` → "Lists" → "Nearby parking": a header with a thumbnail, the title "City Center", a description and the button "Details" (opens the parking pane); on iOS 26.0 the "Featured" section instead of the header.
- [ ] Header: the playback state from the model (`Play`); check that CarPlay does not show "play again" (the binding reads `Replay` for every action; the Stage 9 ledger).
- [ ] The variant `-p:CarKitCategories=…%3BVoiceConversational` → "Voice assistant": an "End" button while listening (closes the modal), "Repeat" and "End" for the answer; "Repeat" returns to listening without closing the modal.
- [ ] `samples/CarKit.Sample.Media` → Playlists → "Live" → the broadcast: the Now Playing screen in sports mode (teams, score 2:1, "2nd half", clock 67:00); after switching to a regular track, back to the cover.
- [ ] `samples/CarKit.Sample.Navigation` → the route "Via the city center with a stop": guidance through two segments, after the stop the phone shows "segment 2/2", the maneuver card keeps working; the detour of the fast route without errors.

### A car with route sharing (iOS 26.4) and video (iOS 27)

- [ ] A car with built-in navigation that supports sharing: the Navigation sample (`nav.ShareRoutesWithCar = true`); the car takes over the route with a stop (the route source in the log "car: route source …"), the charger proposal from the car goes to the log "car: stop …" with an estimate of 1.5 km / 4 min, acceptance/rejection in the log; sharing a destination (`IsDestinationShareable`).
- [ ] iOS 27 and a car with video (the `Video` category, the `carplay-video` entitlement): `CarPlayVideoBrowseTemplate` as a list with a header, items with `CarPlaybackPresentation.Video` played through the app player's AirPlay only while parked; `Supports(CarFeature.VideoPlayback)` = `true` only in a car with video.
- [ ] iOS 27: the MiniPlayer on the Now Playing screen appears by itself; `AllowsMiniPlayer = false` is ignored until the `net10.0-ios27.0` asset (an `Information` log).

### Android Auto Desktop Head Unit (DHU)

- [ ] The main sample: "Voice control" (the message fallback): state actions as message buttons; selecting one closes the screen.
- [ ] Parking list: the "Featured" section instead of the header.
- [ ] The Navigation sample, a route with a stop: the Android Auto navigation card shows the charger first, after the stop the route destination (`Trip` with segment destinations).

## Stage 10 – quickstart guide

Since Stage 10, simulator builds have the CarPlay keys (without them the CarPlay window does not show the app). After changing `CarKitCategories`, delete `bin` and `obj`: an incremental build does not link the new entitlements. The limits depend on the key and the iOS version: an `Audio` app has one alert action in CarPlay (the first one from the model is kept).

The quickstart guide `docs/site/articles/quickstart.md` is executed automatically by `eng/quickstart/run.sh` (iOS and Android builds, scene roles in `Info.plist`, `CarAppService` in the manifest, start on the simulator and emulator). The script cannot see the car screen:

### CarPlay Simulator

1. `eng/quickstart/run.sh`, then in `eng/quickstart/work/MyCarApp`: `dotnet build -t:Run -f net10.0-ios` (`0.1.0-dev` packages from `artifacts/packages`).
2. In Simulator: **I/O → External Displays → CarPlay**.
3. Expected:
   - [ ] The CarPlay screen shows the "MyCarApp" icon; after selecting it, the "Parking nearby" list with three parking lots.
   - [ ] Selecting a parking lot shows a pane with the address and the number of spots, "Reserve" shows an alert, "OK" closes it.

### Android Auto Desktop Head Unit (DHU)

1. `eng/quickstart/run.sh`, then in `eng/quickstart/work/MyCarApp`: `dotnet build -t:Run -f net10.0-android`.
2. The head unit server and `desktop-head-unit` as in the Stage 2 section.
3. Expected:
   - [ ] The DHU launcher shows "MyCarApp"; after selecting it, the same list, Back returns from the pane to the list.
   - [ ] "Reserve" shows an alert (host with Car App API ≥ 5).
