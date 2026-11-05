using CarPlay;
using CoreLocation;
using Foundation;
using MapKit;
using UIKit;

namespace CarKit.Maui.Renderers;

// CarPointOfInterestTemplate → CPPointOfInterestTemplate (spec: "Category decisions (Stage 7)"). Point →
// CPPointOfInterest (MKMapItem, texts, pins, card buttons, UserInfo = Id). The delegate (kept in the state, since it
// is weak on the Objective-C side) looks up a point by UserInfo in the current model, so an in-place update need not
// replace the handlers. Update: title via the setter, changed points via SetPointsOfInterest (new objects), selection
// alone via SelectedIndex. CarPlay reports a selection but not a deselection, so SelectedPointId = null leaves the
// point selected by the driver (by Id; final review I2).
internal static class PointOfInterestTemplateRenderer
{
    public static CarPlayTemplateState Render(CarPointOfInterestTemplate model, CarPlayRenderContext context)
    {
        var state = new CarPlayTemplateState(model);
        CPPointOfInterestTemplate? native = null;
        var points = Points(model, state, context, () => native);
        var selected = IndexOf(model, model.SelectedPointId);
        native = new CPPointOfInterestTemplate(model.Title?.Text ?? string.Empty, points, selected);
        var pointsDelegate = new PointsDelegate(state, context) { SelectedId = selected == NotFound ? null : model.SelectedPointId };
        native.PointOfInterestDelegate = pointsDelegate;
        state.Delegate = pointsDelegate;
        state.Native = native;
        BarButtonRenderer.Apply(native, model, previous: null, state, context);
        return state;
    }

    public static void Update(CarPlayTemplateState state, CPPointOfInterestTemplate native, CarPointOfInterestTemplate model, CarPlayRenderContext context)
    {
        var previous = (CarPointOfInterestTemplate)state.Model;
        if (previous.Title?.Text != model.Title?.Text)
        {
            native.Title = model.Title?.Text ?? string.Empty;
        }

        // An explicit SelectedPointId wins; without it the driver's choice (or the last explicit one) carries over to
        // the same point by Id
        var selection = state.Delegate as PointsDelegate;
        var selectedId = model.SelectedPointId ?? selection?.SelectedId;
        var selected = IndexOf(model, selectedId);
        if (selection is not null)
        {
            selection.SelectedId = selected == NotFound ? null : selectedId;
        }

        if (previous.Points != model.Points || state.Restyled(context))
        {
            native.SetPointsOfInterest(Points(model, state, context, () => native), selected);
        }
        else if (previous.SelectedPointId != model.SelectedPointId)
        {
            native.SelectedIndex = selected;
        }

        BarButtonRenderer.Apply(native, model, previous, state, context);
    }

    private static nint NotFound => (nint)NSRange.NotFound;

    // No selection or an Id outside the points (validator in Warn/Off, point removed) → NSNotFound
    private static nint IndexOf(CarPointOfInterestTemplate model, string? id)
    {
        var index = id is null ? -1 : model.Points.ToList().FindIndex(point => point.Id == id);
        return index >= 0 ? index : NotFound;
    }

    private static CPPointOfInterest[] Points(CarPointOfInterestTemplate model, CarPlayTemplateState state, CarPlayRenderContext context, Func<CPPointOfInterestTemplate?> template) =>
        [.. model.Points.Select(point => Point(point, state, context, template))];

    private static CPPointOfInterest Point(CarPointOfInterest model, CarPlayTemplateState state, CarPlayRenderContext context, Func<CPPointOfInterestTemplate?> template)
    {
        CPPointOfInterest? native = null;
        // A URI/stream image: the pin setter and SetPointsOfInterest again with the same objects (CarPlay refreshes the map)
        var pin = context.Image(state, "pin:" + model.Id, model.PinImage, CarPlayImages.PinSize, loaded =>
        {
            if (native is not null)
            {
                native.PinImage = loaded;
                Refresh(template());
            }
        });
        native = new CPPointOfInterest(
            MapItem(model),
            model.Title.Text,
            model.Subtitle?.Text!,
            model.Summary?.Text!,
            model.DetailTitle?.Text!,
            model.DetailSubtitle?.Text!,
            model.DetailSummary?.Text!,
            pin!)
        {
            UserInfo = new NSString(model.Id),
            PrimaryButton = Button(model.PrimaryAction, state, context)!,
            SecondaryButton = Button(model.SecondaryAction, state, context)!,
        };
        if (OperatingSystem.IsIOSVersionAtLeast(16))
        {
            var selected = context.Image(state, "pin-selected:" + model.Id, model.SelectedPinImage, CarPlayImages.SelectedPinSize, loaded =>
            {
                if (native is not null && OperatingSystem.IsIOSVersionAtLeast(16))
                {
                    native.SelectedPinImage = loaded;
                    Refresh(template());
                }
            });
            if (selected is not null)
            {
                native.SelectedPinImage = selected;
            }
        }

        return native;
    }

    private static void Refresh(CPPointOfInterestTemplate? template) => template?.SetPointsOfInterest(template.PointsOfInterest, template.SelectedIndex);

    // CPTextButton has no disabled state: an action with IsEnabled = false gets no button (as in the information pane)
    private static CPTextButton? Button(CarAction? action, CarPlayTemplateState state, CarPlayRenderContext context) =>
        action is { IsEnabled: true }
            ? new CPTextButton(action.Title.Text, InformationTemplateRenderer.Style(action.Style), _ => context.Select(state, action))
            : null;

    private static MKMapItem MapItem(CarPointOfInterest model)
    {
        var item = OperatingSystem.IsIOSVersionAtLeast(26)
            ? new MKMapItem(new CLLocation(model.Location.Latitude, model.Location.Longitude), null)
            : LegacyMapItem(model.Location);
        item.Name = model.Title.Text;
        return item;
    }

#pragma warning disable CA1422 // MKMapItem(MKPlacemark) is deprecated since iOS 26; this is the path for iOS 15–25
    private static MKMapItem LegacyMapItem(CarLocation location) =>
        new(new MKPlacemark(new CLLocationCoordinate2D(location.Latitude, location.Longitude)));
#pragma warning restore CA1422

    // Native callbacks do not throw: an area outside the model's range (e.g. delta < 0) goes to ICarErrorHandler
    private sealed class PointsDelegate(CarPlayTemplateState state, CarPlayRenderContext context) : CPPointOfInterestTemplateDelegate
    {
        // The last selection: the driver's (DidSelectPointOfInterest) or the model's explicit SelectedPointId; main
        // thread only
        public string? SelectedId { get; set; }

        public override void DidChangeMapRegion(CPPointOfInterestTemplate pointOfInterestTemplate, MKCoordinateRegion region)
        {
            var model = (CarPointOfInterestTemplate)state.Model;
            if (model.OnMapRegionChanged is null)
            {
                return;
            }

            CarMapRegion mapped;
            try
            {
                mapped = new CarMapRegion(new CarLocation(region.Center.Latitude, region.Center.Longitude), region.Span.LatitudeDelta, region.Span.LongitudeDelta);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                context.Errors.HandleError(ex, new CarErrorContext("MapRegionChanged", model));
                return;
            }

            context.Run(model.OnMapRegionChanged, new CarMapRegionContext(context.Session, model, mapped, context.Session.ConnectionToken), "MapRegionChanged");
        }

        public override void DidSelectPointOfInterest(CPPointOfInterestTemplate pointOfInterestTemplate, CPPointOfInterest pointOfInterest)
        {
            if ((pointOfInterest.UserInfo as NSString)?.ToString() is { } id && CarModelWalker.Find(state.Model, id) is CarPointOfInterest point)
            {
                SelectedId = id;
                context.Run(point.OnSelected, context.Session.CreateContext(point), "Select");
            }
        }
    }
}
