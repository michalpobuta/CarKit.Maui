using Android.Content;
using Android.Graphics;
using AndroidX.Car.App.Model;
using AndroidX.Core.Graphics.Drawable;
using NativeCarColor = AndroidX.Car.App.Model.CarColor;

namespace CarKit.Maui.Renderers;

// CarImage → CarIcon (spec: "Template decisions (Stage 3)"): an app drawable (MauiImage, name in the MAUI convention)
// as a resource (the host loads it itself, no bitmap in the Binder), an absolute path, MauiAsset, a Material icon from
// an app drawable, bytes; bitmaps scaled to maxPixels
internal sealed class CarAppImages(Context context) : ICarImagePlatform<CarIcon>
{
    // Bitmap sizes (px): a template with bitmaps goes through the Binder (transaction limit about 1 MB)
    public const int SmallPixels = 128;
    public const int LargePixels = 160;

    public CarIcon? FromFile(string path, int maxPixels)
    {
        if (Drawable(CarImageNames.AndroidResourceName(path)) is { } resource)
        {
            return resource;
        }

        if (System.IO.Path.IsPathRooted(path) && File.Exists(path))
        {
            return FromBytes(File.ReadAllBytes(path), maxPixels);
        }

        try
        {
            using var asset = context.Assets!.Open(path);
            using var buffer = new MemoryStream();
            asset.CopyTo(buffer);
            return FromBytes(buffer.ToArray(), maxPixels);
        }
        catch (Java.IO.FileNotFoundException)
        {
            return null;
        }
    }

    public CarIcon? FromSymbol(CarSymbolImage symbol, int maxPixels)
    {
        // A Material icon added to the app as a drawable; android.R.drawable is not supported, because the public IconCompat
        // stores the app's package (the host would look for the framework resource in the app), and the variant with a
        // package is an internal API
        return symbol.AndroidIconName is { } name ? Drawable(name) : null;
    }

    public CarIcon? FromBytes(byte[] data, int maxPixels)
    {
        var bounds = new BitmapFactory.Options { InJustDecodeBounds = true };
        BitmapFactory.DecodeByteArray(data, 0, data.Length, bounds);
        if (bounds.OutWidth <= 0 || bounds.OutHeight <= 0)
        {
            return null;
        }

        var sample = 1;
        while (Math.Max(bounds.OutWidth, bounds.OutHeight) / (sample * 2) >= maxPixels)
        {
            sample *= 2;
        }

        var bitmap = BitmapFactory.DecodeByteArray(data, 0, data.Length, new BitmapFactory.Options { InSampleSize = sample });
        if (bitmap is null)
        {
            return null;
        }

        var longest = Math.Max(bitmap.Width, bitmap.Height);
        if (longest > maxPixels)
        {
            var factor = (double)maxPixels / longest;
            var scaled = Bitmap.CreateScaledBitmap(bitmap, Math.Max(1, (int)(bitmap.Width * factor)), Math.Max(1, (int)(bitmap.Height * factor)), true)!;
            if (!ReferenceEquals(scaled, bitmap))
            {
                bitmap.Recycle();
            }

            bitmap = scaled;
        }

        return new CarIcon.Builder(IconCompat.CreateWithBitmap(bitmap)!).Build();
    }

    public CarIcon Tint(CarIcon image, CarColor tint)
    {
        var argb = (tint.Alpha << 24) | (tint.Red << 16) | (tint.Green << 8) | tint.Blue;
        return new CarIcon.Builder(image).SetTint(NativeCarColor.CreateCustom(argb, argb)!)!.Build()!;
    }

    // Android chooses the variant by IsDarkMode (CarImageLoader without merging variants)
    public CarIcon Combine(CarIcon light, CarIcon dark, CarContentStyle style) => style == CarContentStyle.Dark ? dark : light;

    // App icon: a tab without an image (Tab requires an icon) and a grid item whose image failed to load
    public static CarIcon AppIcon(Context context) =>
        new CarIcon.Builder(IconCompat.CreateWithResource(context, context.ApplicationInfo!.Icon)!).Build()!;

    private CarIcon? Drawable(string name)
    {
        var id = context.Resources!.GetIdentifier(name, "drawable", context.PackageName);
        return id == 0 ? null : new CarIcon.Builder(IconCompat.CreateWithResource(context, id)!).Build();
    }
}
