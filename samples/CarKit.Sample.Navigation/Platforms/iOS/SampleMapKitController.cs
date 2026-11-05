using CoreLocation;
using MapKit;
using UIKit;

namespace CarKit.Sample.Navigation;

// Native view mode on CarPlay: an MKMapView in a CPWindow (MapKit needs no key or account)
internal sealed class SampleMapKitController(SampleRouteSimulator simulator) : UIViewController
{
    private readonly MKMapView _map = new();

    public override void LoadView() => View = _map;

    public override void ViewDidLoad()
    {
        base.ViewDidLoad();
        var center = simulator.Destination?.Location ?? SampleRoutes.Start;
        _map.SetRegion(new MKCoordinateRegion(new CLLocationCoordinate2D(center.Latitude, center.Longitude), new MKCoordinateSpan(0.05, 0.05)), animated: false);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _map.Dispose();
        }

        base.Dispose(disposing);
    }
}
