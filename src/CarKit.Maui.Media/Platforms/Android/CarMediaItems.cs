using Android.Content;
using Android.OS;
using AndroidX.Media3.Common;
using AndroidX.Media3.Session;
using CarKit.Maui.Renderers;
using Microsoft.Extensions.Logging;
using AndroidUri = Android.Net.Uri;

namespace CarKit.Maui.Media;

// CarMediaItem → Media3 MediaItem (spec: "Media module decisions (Stage 5)"): metadata, browsability, children style,
// explicit, progress and artwork in a form that Android Auto can fetch (android.resource://, content://, http(s))
internal static class CarMediaItems
{
    // Artwork bytes travel through Binder together with the item list
    public const int MaxArtworkBytes = 256 * 1024;

    // Root extras (legacy MediaBrowserService): Android Auto shows search
    public const string SearchSupportedKey = "android.media.browse.SEARCH_SUPPORTED";

    public static MediaItem ToMediaItem(CarMediaItem item, Context context, ILogger logger)
    {
        var extras = new Bundle();
        if (item.IsBrowsable)
        {
            var style = item.ChildrenStyle == CarMediaChildrenStyle.Grid
                ? MediaConstants.ExtrasValueContentStyleGridItem
                : MediaConstants.ExtrasValueContentStyleListItem;
            extras.PutInt(MediaConstants.ExtrasKeyContentStyleBrowsable, style);
            extras.PutInt(MediaConstants.ExtrasKeyContentStylePlayable, style);
        }

        if (item.IsExplicit)
        {
            extras.PutLong(MediaConstants.ExtrasKeyIsExplicit, MediaConstants.ExtrasValueAttributePresent);
        }

        if (item.Progress is { } progress)
        {
            extras.PutInt(MediaConstants.ExtrasKeyCompletionStatus, progress switch
            {
                <= 0 => MediaConstants.ExtrasValueCompletionStatusNotPlayed,
                >= 1 => MediaConstants.ExtrasValueCompletionStatusFullyPlayed,
                _ => MediaConstants.ExtrasValueCompletionStatusPartiallyPlayed,
            });
            extras.PutDouble(MediaConstants.ExtrasKeyCompletionPercentage, progress);
        }

        var metadata = new MediaMetadata.Builder()
            .SetTitle(item.Title)!
            .SetDisplayTitle(item.Title)!
            .SetIsBrowsable(Java.Lang.Boolean.ValueOf(item.IsBrowsable))!
            .SetIsPlayable(Java.Lang.Boolean.ValueOf(item.IsPlayable))!
            .SetMediaType(Java.Lang.Integer.ValueOf(item.IsBrowsable && !item.IsPlayable ? MediaMetadata.MediaTypeFolderMixed : MediaMetadata.MediaTypeMusic))!
            .SetExtras(extras)!;
        if (item.Subtitle is { } subtitle)
        {
            metadata.SetSubtitle(subtitle)!.SetArtist(subtitle);
        }

        switch (Artwork(item, context, logger))
        {
            case AndroidUri uri:
                metadata.SetArtworkUri(uri);
                break;
            case byte[] bytes:
                metadata.SetArtworkData(bytes, Java.Lang.Integer.ValueOf(MediaMetadata.PictureTypeFrontCover));
                break;
        }

        return new MediaItem.Builder().SetMediaId(item.Id)!.SetMediaMetadata(metadata.Build()!)!.Build()!;
    }

    // Uri, bytes or null (none, a stream, a resource that does not exist)
    private static object? Artwork(CarMediaItem item, Context context, ILogger logger)
    {
        switch (item.Artwork)
        {
            case null:
                return null;
            case CarFileImage file:
                return Resource(CarImageNames.AndroidResourceName(file.Path), item, context, logger);
            case CarSymbolImage { AndroidIconName: { } icon }:
                return Resource(icon, item, context, logger);
            case CarUriImage { Uri: var uri } when uri.IsAbsoluteUri && uri.Scheme is "http" or "https" or "content" or "android.resource":
                return AndroidUri.Parse(uri.AbsoluteUri);
            case CarBytesImage { Data: var data } when data.Count <= MaxArtworkBytes:
                return data.ToArray();
            default:
                CarMediaLog.ArtworkSkipped(logger, item.Id, "Android Auto loads artwork from app resources, http(s) or content URIs and small byte arrays");
                return null;
        }
    }

    private static AndroidUri? Resource(string name, CarMediaItem item, Context context, ILogger logger)
    {
        if (context.Resources?.GetIdentifier(name, "drawable", context.PackageName) is not > 0)
        {
            CarMediaLog.ArtworkSkipped(logger, item.Id, $"no drawable resource '{name}'");
            return null;
        }

        return AndroidUri.Parse($"android.resource://{context.PackageName}/drawable/{name}");
    }
}
