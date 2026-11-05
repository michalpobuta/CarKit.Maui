# Media

`CarKit.Maui.Media` turns the app's media library and player into the CarPlay and Android Auto media experience
(category `Audio`). The app keeps its own player; CarKit adapts it.

```xml
<CarKitCategories>Audio</CarKitCategories>
```

```csharp
using CarKit.Maui;
using CarKit.Maui.Media;

builder.UseMauiApp<App>().UseCarKit(car => car.UseMedia(media =>
{
    media.UseLibrary<MyMediaLibrary>();   // ICarMediaLibrary
    media.UsePlayer<MyMediaPlayer>();     // ICarMediaPlayer
    media.UseAutoBrowse();                // CarKit builds the CarPlay browsing screens
}));
```

Without `UseAutoBrowse()` the app keeps `car.UseHandler<T>()` and builds the screens itself. `Audio` without the package
produces the warning `CARKIT005`.

## The two interfaces

- `ICarMediaLibrary`: `GetRootAsync`, `GetChildrenAsync(parentId, page, pageSize)`, `SearchAsync`, `GetItemAsync`. Items are
  `CarMediaItem` (`Id`, `Title`, `Subtitle`, `Artwork`, `IsPlayable`, `IsBrowsable`, `ChildrenStyle`, `Duration`,
  `IsExplicit`, `Progress`, `IsPlaying`, `Sports`).
- `ICarMediaPlayer`: an adapter over the app's player. `State` emits the current `CarPlaybackState` on subscription and
  every change, from any thread; the commands are `PlayAsync(itemId)`, pause, resume, seek, skip, shuffle, repeat, rate
  and `InvokeCustomActionAsync`.

Both are singletons, so the phone UI and the car share one player.

## CarPlay

- Auto-browse: the root's children become tabs (when there are 2 up to the tab limit and all are browsable) or one list;
  browsable rows push the children, playable rows call `PlayAsync` and show Now Playing. Pages end with a "More" row.
- Now Playing: `MPNowPlayingInfoCenter` and `MPRemoteCommandCenter` from app start, also without CarPlay (title, artist,
  artwork, position, rate, queue). `CarNowPlayingTemplate` maps to `CPNowPlayingTemplate.SharedTemplate` with shuffle,
  repeat, playback rate, add-to-library, more and custom buttons, "Up Next" and "Album/Artist".
- Sports mode (iOS 18.4): `CarNowPlayingTemplate.Sports` or `CarMediaItem.Sports` of the playing item.
- `AVAudioSession` uses the `Playback` category. The build adds `UIBackgroundModes` = `audio`.

## Android Auto

- A generated `MediaLibraryService` (Media3) serves the library to Android Auto: browsing with list or grid content
  style, search, paging and artwork. Android Auto draws the media UI itself.
- `CarKitPlayer` adapts `ICarMediaPlayer` to the media session; the buttons of `media.NowPlaying` become session
  commands.
- Templated media (`MediaPlaybackTemplate`, Car App API 8) is off by default: Google offers media apps built with Car App
  Library templates only through its Early Access Program, published to internal and closed testing tracks. Enable it
  with `<CarKitAndroidTemplatedMedia>true</CarKitAndroidTemplatedMedia>`.

## Audio focus and interruptions

The module pauses the player when another app takes audio focus or a call starts (iOS interruption, Android transient
loss or duck), resumes it when the interruption ends, and pauses without resuming when the output disconnects
(headphones, Bluetooth). On Android it requests `AUDIOFOCUS_GAIN` when playback starts; set `media.HandleAudioFocus =
false` when the app's player manages focus itself (for example ExoPlayer with `handleAudioFocus`).

## Options

| Option | Default | Meaning |
| --- | --- | --- |
| `media.PlaybackRates` | `[1]` | Rates of the playback rate button |
| `media.NowPlaying` | Shuffle and repeat buttons (plus rate with more than one rate) | Now Playing template of auto-browse and the media session buttons |
| `media.UseTemplatedMediaOnAndroid` | From `CarKitAndroidTemplatedMedia` | Read-only in practice; a different value fails at startup |
| `media.HandleAudioFocus` | `true` | CarKit manages Android audio focus |
