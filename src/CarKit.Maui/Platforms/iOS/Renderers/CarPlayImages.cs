using CarPlay;
using CoreGraphics;
using Foundation;
using ImageIO;
using UIKit;

namespace CarKit.Maui.Renderers;

// CarImage → UIImage (spec: "Template decisions (Stage 3)"): app bundle (MauiImage, .png for .svg), absolute
// path, SF Symbol, bytes; scaling to the template's max size × the car screen scale; dark variant in UIImageAsset
internal sealed class CarPlayImages(nfloat scale) : ICarImagePlatform<UIImage>
{
    // Sizes without a platform API (points): bar button, tab, grid before iOS 26
    public static readonly CGSize BarButtonSize = new(32, 32);
    public static readonly CGSize TabSize = new(30, 30);

    public static CGSize GridSize => OperatingSystem.IsIOSVersionAtLeast(26) ? CPGridTemplate.MaximumGridButtonImageSize : new CGSize(40, 40);

    // Stage 7: CPPointOfInterest pins (sizes from the API since iOS 16; earlier 40 pt as in the pinImage documentation)
    public static CGSize PinSize => OperatingSystem.IsIOSVersionAtLeast(16) ? CPPointOfInterest.PinImageSize : new CGSize(40, 40);

    public static CGSize SelectedPinSize => OperatingSystem.IsIOSVersionAtLeast(16) ? CPPointOfInterest.SelectedPinImageSize : new CGSize(40, 40);

    public UIImage? FromFile(string path, int maxPixels)
    {
        foreach (var candidate in CarImageNames.IosBundleCandidates(path))
        {
            if (UIImage.FromBundle(candidate) is { } bundled)
            {
                return Scale(bundled, maxPixels);
            }
        }

        return Path.IsPathRooted(path) && File.Exists(path) && UIImage.FromFile(path) is { } file ? Scale(file, maxPixels) : null;
    }

    public UIImage? FromSymbol(CarSymbolImage symbol, int maxPixels) =>
        symbol.IosSymbolName is { } name
            ? UIImage.GetSystemImage(name, UIImageSymbolConfiguration.Create(nfloat.Max(maxPixels / scale * 0.6f, 12)))
            : null;

    // Also called from the thread pool (URI and stream images): ImageIO instead of UIKit (UIGraphicsImageRenderer on
    // the main thread only)
    public UIImage? FromBytes(byte[] data, int maxPixels)
    {
        using var nsData = NSData.FromArray(data);
        using var source = CGImageSource.FromData(nsData);
        if (source is null || source.ImageCount == 0)
        {
            return null;
        }

        using var thumbnail = source.CreateThumbnail(0, new CGImageThumbnailOptions
        {
            MaxPixelSize = maxPixels,
            CreateThumbnailFromImageAlways = true,
            CreateThumbnailWithTransform = true,
        });
        return thumbnail is null ? null : UIImage.FromImage(thumbnail, scale, UIImageOrientation.Up);
    }

    // Tint and UIImageAsset are UIKit APIs for the main thread only; URI/stream images end up on the thread pool,
    // so these two steps (fast, after decoding) go synchronously to the main thread
    public UIImage Tint(UIImage image, CarColor tint) => OnMain(() =>
        image.ApplyTintColor(UIColor.FromRGBA(tint.Red, tint.Green, tint.Blue, tint.Alpha), UIImageRenderingMode.AlwaysOriginal));

    public UIImage Combine(UIImage light, UIImage dark, CarContentStyle style) => OnMain(() =>
    {
        var asset = new UIImageAsset();
        asset.RegisterImage(light, UITraitCollection.FromUserInterfaceStyle(UIUserInterfaceStyle.Light));
        asset.RegisterImage(dark, UITraitCollection.FromUserInterfaceStyle(UIUserInterfaceStyle.Dark));
        return asset.FromTraitCollection(UITraitCollection.FromUserInterfaceStyle(
            style == CarContentStyle.Dark ? UIUserInterfaceStyle.Dark : UIUserInterfaceStyle.Light));
    });

    // CPGridButton and image row elements require an image; until it loads we show a transparent one
    public static UIImage Placeholder(CGSize size, nfloat scale)
    {
        using var renderer = new UIGraphicsImageRenderer(size, new UIGraphicsImageRendererFormat { Scale = scale, Opaque = false });
        return renderer.CreateImage(_ => { });
    }

    // Main thread only (bundle files)
    private UIImage Scale(UIImage image, int maxPixels)
    {
        var size = image.Size;
        var pixels = nfloat.Max(size.Width, size.Height) * image.CurrentScale;
        if (pixels <= maxPixels || size.Width <= 0 || size.Height <= 0)
        {
            return image;
        }

        var factor = maxPixels / pixels * image.CurrentScale / scale;
        var target = new CGSize(nfloat.Max(size.Width * factor, 1), nfloat.Max(size.Height * factor, 1));
        using var renderer = new UIGraphicsImageRenderer(target, new UIGraphicsImageRendererFormat { Scale = scale, Opaque = false });
        return renderer.CreateImage(_ => image.Draw(new CGRect(CGPoint.Empty, target)));
    }

    private static UIImage OnMain(Func<UIImage> work)
    {
        if (NSThread.IsMain)
        {
            return work();
        }

        UIImage? result = null;
        NSThread.MainThread.InvokeOnMainThread(() => result = work());
        return result!;
    }
}
