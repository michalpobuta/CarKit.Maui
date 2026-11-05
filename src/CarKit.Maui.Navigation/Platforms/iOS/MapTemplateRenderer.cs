using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using CarKit.Maui.Renderers;
using CarPlay;
using CoreGraphics;
using Foundation;
using Microsoft.Maui.Graphics;
using ObjCRuntime;
using UIKit;

namespace CarKit.Maui.Navigation;

// CarMapTemplate → CPMapTemplate (spec: "Navigation module decisions (Stage 8)", CarMapTemplate on CarPlay): map
// buttons, bar buttons, pane color, hiding the bar; the map delegate in the renderer state (it is weak on the
// Objective-C side). Updates always in place (all properties have setters). Navigation on the template is driven by
// the connection sink.
internal sealed partial class MapTemplateRenderer(CarPlayNavigationModule module) : ICarPlayModuleRenderer
{
    // CPMapButton image size (CarPlay draws map buttons at about 34 pt)
    private static readonly CGSize _mapButtonSize = new(34, 34);

    public bool CanRender(CarTemplate template) => template is CarMapTemplate;

    public CarPlayTemplateState Render(CarTemplate template, CarPlayRenderContext context)
    {
        var model = (CarMapTemplate)template;
        var native = CreateMapTemplate();
        var state = new CarPlayTemplateState(model) { Native = native };
        var mapDelegate = new MapDelegate(module, context);
        state.Delegate = mapDelegate;
        native.MapDelegate = mapDelegate;
        Apply(state, native, model, previous: null, context);
        module.OnMapTemplateRendered(context, native, state);
        return state;
    }

    public bool UpdateInPlace(CarPlayTemplateState state, CarTemplate updated, CarPlayRenderContext context)
    {
        var native = (CPMapTemplate)state.Native;
        Apply(state, native, (CarMapTemplate)updated, (CarMapTemplate)state.Model, context);
        module.OnMapTemplateRendered(context, native, state);
        return true;
    }

    // Bindings 26.0 and 26.4 have no public parameterless constructor (only NSCoder/NSObjectFlag/NativeHandle), and a
    // subclass would be a class unknown to CarPlay: +[CPMapTemplate new] through objc_msgSend, the object is taken over
    // by GetNSObject (owns: +1 from new)
    internal static CPMapTemplate CreateMapTemplate()
    {
        var handle = ObjCMessages.Send(Class.GetHandle("CPMapTemplate"), Selector.GetHandle("new"));
        return Runtime.GetNSObject<CPMapTemplate>(handle, owns: true)
            ?? throw new InvalidOperationException("CarPlay did not create a CPMapTemplate.");
    }

    private static void Apply(CarPlayTemplateState state, CPMapTemplate native, CarMapTemplate model, CarMapTemplate? previous, CarPlayRenderContext context)
    {
        native.AutomaticallyHidesNavigationBar = model.AutomaticallyHidesNavigationBar;
        native.TripEstimateStyle = context.Style == CarContentStyle.Dark ? CPTripEstimateStyle.Dark : CPTripEstimateStyle.Light;
        if (model.GuidanceBackgroundColor is { } color)
        {
            native.GuidanceBackgroundColor = UIColor.FromRGBA(color.Red, color.Green, color.Blue, color.Alpha);
        }

        if (previous is null || previous.MapButtons != model.MapButtons || state.Restyled(context))
        {
            native.MapButtons = [.. model.MapButtons.Select(button => MapButton(button, state, context))];
        }

        BarButtonRenderer.Apply(native, model, previous, state, context);
    }

    private static CPMapButton MapButton(CarMapButton model, CarPlayTemplateState state, CarPlayRenderContext context)
    {
        CPMapButton? native = null;
        native = new CPMapButton(_ => Press(state, model, context))
        {
            Hidden = model.IsHidden,
            Enabled = model.IsEnabled,
        };
        if (context.Image(state, "map:" + model.Id, model.Image, _mapButtonSize, loaded => native.Image = loaded) is { } image)
        {
            native.Image = image;
        }

        if (model.FocusedImage is { } focused && context.Image(state, "map-focused:" + model.Id, focused, _mapButtonSize, loaded => native.FocusedImage = loaded) is { } focusedImage)
        {
            native.FocusedImage = focusedImage;
        }

        return native;
    }

    // CPMapButton handler (also from device tests; the binding does not expose the button block): the current version
    // of the button
    internal static void Press(CarPlayTemplateState state, CarMapButton rendered, CarPlayRenderContext context)
    {
        var current = state.Current(rendered);
        context.Run(current.OnSelected, context.Session.CreateContext(current), "Select");
    }

    // CPMapTemplate delegate: preview, start and cancel → navigation session (app handler), gestures → CarScreen,
    // background notifications and cluster metadata enabled. Native callbacks do not throw.
    private sealed class MapDelegate(CarPlayNavigationModule module, CarPlayRenderContext context) : CPMapTemplateDelegate
    {
        private readonly CarMapGestureTranslator _gestures = new();

        private CarNavigationRuntime Runtime => module.Runtime;

        private CarMapSurface Surface => Runtime.Surfaces.CarScreen;

        public override void SelectedPreview(CPMapTemplate mapTemplate, CPTrip trip, CPRouteChoice routeChoice) =>
            Runtime.Navigation.OnPreviewSelectedAsync(Id(trip.UserInfo), Id(routeChoice.UserInfo)).Forget(context.Errors, "Navigation.TripPreviewSelected");

        public override void StartedTrip(CPMapTemplate mapTemplate, CPTrip trip, CPRouteChoice routeChoice) =>
            Runtime.Navigation.OnStartRequestedAsync(Id(trip.UserInfo), Id(routeChoice.UserInfo)).Forget(context.Errors, "Navigation.TripStartRequested");

        public override void DidCancelNavigation(CPMapTemplate mapTemplate)
        {
            module.OnNavigationCancelledByCar(context);
            Runtime.Navigation.OnEndedByCarAsync().Forget(context.Errors, "Navigation.EndedByCar");
        }

        public override void DidDismissNavigationAlert(CPMapTemplate mapTemplate, CPNavigationAlert navigationAlert, CPNavigationAlertDismissalContext dismissalContext)
        {
            if (module.ForgetAlert(navigationAlert) is { } id)
            {
                Runtime.Navigation.OnAlertDismissed(id);
            }
        }

        public override bool ShouldShowNotificationForManeuver(CPMapTemplate mapTemplate, CPManeuver maneuver) => true;

        public override bool ShouldUpdateNotificationForManeuver(CPMapTemplate mapTemplate, CPManeuver maneuver, CPTravelEstimates travelEstimates) => true;

        public override bool ShouldShowNotificationForNavigationAlert(CPMapTemplate mapTemplate, CPNavigationAlert navigationAlert) => true;

        // iOS 17.4: CarPlay passes maneuvers from CPNavigationSession to the cluster and car displays
        public override bool ShouldProvideNavigationMetadata(CPMapTemplate mapTemplate) => true;

#if IOS26_4_OR_GREATER
        // Stage 9: route sharing with the car (iOS 26.4), only with nav.ShareRoutesWithCar; car requests →
        // ICarNavigationHandler
        [SupportedOSPlatform("ios26.4")]
        [UnsupportedOSPlatform("maccatalyst")]
        public override bool ShouldProvideRouteSharing(CPMapTemplate mapTemplate) => Runtime.Options.ShareRoutesWithCar;

        [SupportedOSPlatform("ios26.4")]
        [UnsupportedOSPlatform("maccatalyst")]
        public override void DidRequestToInsertWaypoint(
            CPMapTemplate mapTemplate, CPNavigationWaypoint waypoint, CPRouteSegment segment, CPMapTemplateDidRequestToInsertWaypointHandler completion) =>
            InsertWaypointAsync(waypoint, segment, completion).Forget(context.Errors, "Navigation.WaypointInsertRequested");

        [SupportedOSPlatform("ios26.4")]
        [UnsupportedOSPlatform("maccatalyst")]
        public override void MapTemplateWaypoint(CPMapTemplate mapTemplate, CPNavigationWaypoint waypoint, bool accepted, CPRouteSegment? segment) =>
            Runtime.Navigation.OnWaypointProposalAnsweredAsync(CarPlayNavigationModels.FromNative(waypoint), accepted, module.SinkFor(context)?.SegmentIndex(segment))
                .Forget(context.Errors, "Navigation.WaypointProposalAnswered");

        [SupportedOSPlatform("ios26.4")]
        [UnsupportedOSPlatform("maccatalyst")]
        public override void DidReceiveRequestForDestination(CPMapTemplate mapTemplate, CPNavigationWaypoint waypoint) =>
            Runtime.Navigation.OnDestinationRequestedAsync(CarPlayNavigationModels.FromNative(waypoint)).Forget(context.Errors, "Navigation.DestinationRequested");

        [SupportedOSPlatform("ios26.4")]
        [UnsupportedOSPlatform("maccatalyst")]
        public override void DidReceiveUpdatedRouteSource(CPMapTemplate mapTemplate, CPRouteSource routeSource) =>
            Runtime.Navigation.OnRouteSourceChangedAsync(CarPlayNavigationModels.RouteSource(routeSource)).Forget(context.Errors, "Navigation.RouteSourceChanged");

        [SupportedOSPlatform("ios26.4")]
        [UnsupportedOSPlatform("maccatalyst")]
        public override void WillShareDestination(CPMapTemplate mapTemplate, CPTrip trip) => Trace("will share destination of " + trip.UserInfo);

        [SupportedOSPlatform("ios26.4")]
        [UnsupportedOSPlatform("maccatalyst")]
        public override void DidShareDestination(CPMapTemplate mapTemplate, CPTrip trip) => Trace("shared destination of " + trip.UserInfo);

        [SupportedOSPlatform("ios26.4")]
        [UnsupportedOSPlatform("maccatalyst")]
        public override void DidFailToShareDestination(CPMapTemplate mapTemplate, CPTrip trip, NSError error) =>
            Trace($"could not share destination of {trip.UserInfo}: {error.LocalizedDescription}");

        // CarPlay completion always: the app's estimate or a zero one (no handler; an exception → ICarErrorHandler in
        // the session)
        [SupportedOSPlatform("ios26.4")]
        [UnsupportedOSPlatform("maccatalyst")]
        private async Task InsertWaypointAsync(CPNavigationWaypoint waypoint, CPRouteSegment segment, CPMapTemplateDidRequestToInsertWaypointHandler completion)
        {
            CarTravelEstimates? estimates = null;
            try
            {
                var index = module.SinkFor(context)?.SegmentIndex(segment) ?? Runtime.Navigation.CurrentSegmentIndex;
                estimates = await Runtime.Navigation.OnWaypointInsertRequestedAsync(CarPlayNavigationModels.FromNative(waypoint), index);
            }
            finally
            {
                completion(CarPlayNavigationModels.Estimates(estimates ?? new CarTravelEstimates(new CarDistance(0, CarDistanceUnit.Meters), TimeSpan.Zero)));
            }
        }

        private void Trace(string message) => CarNavigationLog.Trace(Runtime.Logger, message);
#endif

        public override void DidShowPanningInterface(CPMapTemplate mapTemplate) => Surface.SetPanning(true);

        public override void DidDismissPanningInterface(CPMapTemplate mapTemplate) => Surface.SetPanning(false);

        public override void DidBeginPanGesture(CPMapTemplate mapTemplate) => _gestures.PanBegan();

        public override void DidUpdatePanGesture(CPMapTemplate mapTemplate, CGPoint translation, CGPoint velocity)
        {
            if (_gestures.PanUpdated(new PointF((float)translation.X, (float)translation.Y)) is { } gesture)
            {
                Surface.RaiseGesture(gesture);
            }
        }

        public override void DidEndPanGesture(CPMapTemplate mapTemplate, CGPoint velocity) =>
            Surface.RaiseGesture(CarMapGestureTranslator.PanEnded(new PointF((float)velocity.X, (float)velocity.Y)));

        public override void Pan(CPMapTemplate mapTemplate, CPPanDirection direction)
        {
            if (Direction(direction) is { } pan)
            {
                Surface.RaiseGesture(CarMapGestureTranslator.PanStep(pan, Surface.Size));
            }
        }

        public override void DidBeginZoomGesture(CPMapTemplate mapTemplate) => _gestures.ZoomBegan();

        public override void DidUpdateZoomGesture(CPMapTemplate mapTemplate, CGPoint center, nfloat scale, nfloat velocity)
        {
            if (_gestures.ZoomUpdated(new PointF((float)center.X, (float)center.Y), (float)scale) is { } gesture)
            {
                Surface.RaiseGesture(gesture);
            }
        }

        private static string Id(NSObject? userInfo) => userInfo?.ToString() ?? string.Empty;

        private static CarPanDirection? Direction(CPPanDirection direction) => direction switch
        {
            CPPanDirection.Left => CarPanDirection.Left,
            CPPanDirection.Right => CarPanDirection.Right,
            CPPanDirection.Up => CarPanDirection.Up,
            CPPanDirection.Down => CarPanDirection.Down,
            _ => null,
        };
    }
}

// P/Invoke to libobjc (AOT-compatible): the binding does not expose +new for CPMapTemplate, and the CPLane
// constructor with a highlighted angle has the wrong parameter type in the binding
internal static partial class ObjCMessages
{
    [DllImport(Constants.ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern IntPtr IntPtr_objc_msgSend(IntPtr receiver, IntPtr selector);

    [DllImport(Constants.ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern IntPtr IntPtr_objc_msgSend_IntPtr_IntPtr_byte(IntPtr receiver, IntPtr selector, IntPtr arg1, IntPtr arg2, byte arg3);

    public static IntPtr Send(IntPtr receiver, IntPtr selector) => IntPtr_objc_msgSend(receiver, selector);

    // -[CPLane initWithAngles:highlightedAngle:isPreferred:] (iOS 18); alloc+init gives an object with a retain count
    // of 1 → owns: true
    [System.Runtime.Versioning.SupportedOSPlatform("ios18.0")]
    [System.Runtime.Versioning.UnsupportedOSPlatform("maccatalyst")]
    public static CPLane Lane(NSMeasurement<NSUnitAngle>[] angles, NSMeasurement<NSUnitAngle> highlighted, bool preferred)
    {
        using var array = NSArray.FromNSObjects(angles);
        var allocated = IntPtr_objc_msgSend(Class.GetHandle(typeof(CPLane)), Selector.GetHandle("alloc"));
        var lane = IntPtr_objc_msgSend_IntPtr_IntPtr_byte(
            allocated, Selector.GetHandle("initWithAngles:highlightedAngle:isPreferred:"), array.Handle, highlighted.Handle, preferred ? (byte)1 : (byte)0);
        GC.KeepAlive(highlighted);
        return Runtime.GetNSObject<CPLane>(lane, owns: true) ?? throw new InvalidOperationException("CPLane initWithAngles:highlightedAngle:isPreferred: returned nil.");
    }
}
