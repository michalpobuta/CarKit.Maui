using CarPlay;

namespace CarKit.Maui;

// CPInterfaceController events (e.g. the CarPlay Back button) → host
internal sealed class CarPlayInterfaceDelegate(CarPlayHost host) : CPInterfaceControllerDelegate
{
    public override void TemplateDidDisappear(CPTemplate aTemplate, bool animated) => host.OnTemplateDidDisappear(aTemplate);
}
