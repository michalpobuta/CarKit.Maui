using Android.Content;
using AndroidView = Android.Views.View;

namespace CarKit.Maui.Navigation;

internal sealed partial class CarMapSurface
{
    public void UseView(Func<Context, AndroidView> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        SetContent(new CarMapSurfaceContent.Native(factory));
    }
}
