using Android.Runtime;
using AndroidX.Media3.Common;

namespace CarKit.Maui.Media;

// The MediaItem.requestMetadata field is not in the Media3 1.9.0 binding (name collision with the nested type
// RequestMetadata), so the voice query (legacy onPlayFromSearch → RequestMetadata.searchQuery) is read through JNI,
// without .NET reflection
internal static class CarMediaRequests
{
    private static IntPtr _field;

    public static string? SearchQuery(MediaItem item)
    {
        if (_field == IntPtr.Zero)
        {
            var type = JNIEnv.FindClass("androidx/media3/common/MediaItem");
            try
            {
                _field = JNIEnv.GetFieldID(type, "requestMetadata", "Landroidx/media3/common/MediaItem$RequestMetadata;");
            }
            finally
            {
                JNIEnv.DeleteGlobalRef(type);
            }
        }

        var handle = JNIEnv.GetObjectField(item.Handle, _field);
        using var metadata = Java.Lang.Object.GetObject<MediaItem.RequestMetadata>(handle, JniHandleOwnership.TransferLocalRef);
        return metadata?.SearchQuery;
    }
}
