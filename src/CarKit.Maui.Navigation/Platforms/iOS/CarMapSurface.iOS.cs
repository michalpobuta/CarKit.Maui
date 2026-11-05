using UIKit;

namespace CarKit.Maui.Navigation;

internal sealed partial class CarMapSurface
{
    public void UseViewController(Func<UIViewController> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        SetContent(new CarMapSurfaceContent.Native(factory));
    }
}
