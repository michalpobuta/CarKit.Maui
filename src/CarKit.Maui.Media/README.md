# CarKit.Maui.Media

Media apps for CarPlay and Android Auto (category `Audio`): your `ICarMediaLibrary` and `ICarMediaPlayer` become the
CarPlay library and Now Playing screen (`MPNowPlayingInfoCenter`, `MPRemoteCommandCenter`, `AVAudioSession`) and an
Android Auto media service on Media3 (`MediaLibraryService`, generated for the app). Audio focus, interruptions and
ducking are handled on both platforms.

```xml
<CarKitCategories>Audio</CarKitCategories>
```

```csharp
using CarKit.Maui;
using CarKit.Maui.Media;

builder.UseMauiApp<App>().UseCarKit(car => car.UseMedia(media => media
    .UseLibrary<MyMediaLibrary>()
    .UsePlayer<MyMediaPlayer>()
    .UseAutoBrowse()));
```

Templated media on Android Auto (`MediaPlaybackTemplate`) is behind `<CarKitAndroidTemplatedMedia>true</CarKitAndroidTemplatedMedia>`
because Google offers it only in an Early Access Program.

## Documentation

See the "Media" page of the documentation site in the source repository (`docs/site`).
