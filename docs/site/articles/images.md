# Images

Rows, grid buttons, tabs, actions, points of interest and Now Playing artwork take a <xref:CarKit.Maui.CarImage>.

## Sources

| Factory | Loads | Notes |
| --- | --- | --- |
| `CarImage.FromFile("station.png")` | Immediately | App resource (`MauiImage`: an Android drawable named after the file, an iOS bundle file, also the `.png` generated from an `.svg`), then an absolute path, then a `MauiAsset` |
| `CarImage.FromSystemSymbol("parkingsign", "ic_parking")` | Immediately | An SF Symbol on CarPlay; an Android drawable of the app with the given name on Android Auto (add the Material icon to the app; `android.R.drawable` is not supported) |
| `CarImage.FromBytes(bytes)` | Immediately | Encoded image (PNG, JPEG) |
| `CarImage.FromUri(new Uri("https://…"))` | Asynchronously, off the main thread | `http`, `https` and `file`; at most 5 MB and 20 s |
| `CarImage.FromStream(ct => OpenAsync(ct))` | Asynchronously, off the main thread | At most 5 MB and 20 s. CarKit may call the factory more than once, for example once per image size when the same image is shown in a list row and a grid button, so **return a new stream on every call**; CarKit disposes it |
| `imageSource.ToCarImage()` | — | MAUI `FileImageSource`, `UriImageSource` and `StreamImageSource`; other sources (such as `FontImageSource`) throw `NotSupportedException` |

`Dark` adds a variant for the dark car interface and `Tint` a tint color:

```csharp
var icon = CarImage.FromFile("garage.png") with { Dark = CarImage.FromFile("garage_dark.png"), Tint = new CarColor(0x1E, 0x88, 0xE5) };
```

On CarPlay, CarKit combines both variants in a `UIImageAsset` and sets the images again when the car switches between
light and dark. On Android Auto, it picks the variant for `CarContext.IsDarkMode` and rebuilds the screens after a theme
change (a refresh, not a task step).

## Loading and placeholders

Until an asynchronous image arrives, the element has no image (an Android Auto grid item shows its loading state and
has no click listener, which the host requires for loading items). Then CarKit updates the element in place on CarPlay
— also after in-place updates of the template, as long as the element still expects the same image — or refreshes the
screen on Android Auto.

Images are cached per car connection (LRU, 64 entries). A failed image is logged as a warning and the element stays
without an image; it never throws. The failed result is cached too, so an app that starts offline gets the image again
after the next connection.

## Sizes

- CarPlay: the maximum size of the template element (for example `CPListItem.MaximumImageSize`) times the scale of the
  car screen.
- Android Auto: bitmaps are scaled to at most 128 px (rows, tabs, actions, alerts) or 160 px (grid, message), because a
  template with bitmaps travels through Binder (about 1 MB per transaction). Resource images are sent as resources,
  not bitmaps.
