using CarKit.Maui.Renderers;
using CarPlay;
using CoreGraphics;
using CoreLocation;
using Foundation;
using MapKit;
using UIKit;

namespace CarKit.Maui.Navigation;

// Navigation of one CarPlay connection on the newest CPMapTemplate (spec: "Navigation module decisions (Stage 8)",
// Navigation on CarPlay). Without a map template it remembers the state and replays it when a template appears
// (connection, new root). Main thread.
internal sealed class CarPlayNavigationSink(CarPlayNavigationModule module, CarPlayRenderContext context) : ICarNavigationSink
{
    private static readonly CGSize _symbolSize = new(50, 50);
    private static readonly CGSize _junctionSize = new(140, 100);
    private static readonly CGSize _alertImageSize = new(40, 40);
    private readonly Dictionary<string, CPTrip> _trips = [];
    // Maneuvers already passed to the session (AddManeuvers, iOS 17.4: cluster metadata)
    private readonly HashSet<string> _addedManeuvers = [];
    private CPMapTemplate? _map;
    private CarPlayTemplateState? _mapState;
    private CPNavigationSession? _session;
    private CarNavigationSnapshot _state = CarNavigationSnapshot.Idle;
    private bool _closed;

    // The most recently set maneuvers (for maneuver estimates)
    private CPManeuver[] _upcoming = [];

#if IOS26_4_OR_GREATER
    // Stage 9: route segments passed to the session (iOS 26.4), in the order of CarNavigationSegments.Legs
    private CPRouteSegment[] _segments = [];

    internal CPRouteSegment[] RouteSegments => _segments;
#endif

    // Device tests: native objects without a CarPlay connection
    internal CPNavigationSession? Session => _session;

    internal IReadOnlyDictionary<string, CPTrip> Trips => _trips;

    internal CPManeuver[] Upcoming => _upcoming;

    public void UseMap(CPMapTemplate map, CarPlayTemplateState state)
    {
        if (_closed || ReferenceEquals(map, _map))
        {
            _mapState = state;
            return;
        }

        _map = map;
        _mapState = state;
        _session = null;
        _trips.Clear();
        _addedManeuvers.Clear();
        Show(_state);
    }

    public void ForgetSession() => _session = null;

    public void Close()
    {
        _closed = true;
        _map = null;
        _mapState = null;
        _session = null;
        _trips.Clear();
    }

    public void Restore(CarNavigationSnapshot state)
    {
        _state = state;
        if (_map is not null)
        {
            Show(state);
        }
    }

    public void Apply(CarNavigationChange change, CarNavigationSnapshot state)
    {
        _state = state;
        if (_closed || _map is not { } map)
        {
            return;
        }

        switch (change)
        {
            case CarNavigationChange.PreviewsShown previews:
                ShowPreviews(map, previews.Trips, previews.Selected);
                break;
            case CarNavigationChange.PreviewsHidden:
                map.HideTripPreviews();
                break;
            case CarNavigationChange.PreviewSelected selected when _trips.TryGetValue(selected.Trip.Id, out var selectedTrip):
                // M8 (review): the trip estimate is the estimate of the route chosen by the driver
                if (selected.Route.Estimates is { } routeEstimates)
                {
                    map.UpdateTravelEstimates(CarPlayNavigationModels.Estimates(routeEstimates), selectedTrip, CarPlayNavigationModels.Color(routeEstimates.TimeRemainingColor));
                }

                break;
            case CarNavigationChange.TripStarted started:
                Start(map, started.Trip, state);
                break;
            case CarNavigationChange.ManeuversUpdated updated:
                Maneuvers(updated.Maneuvers, state, resume: updated.Resumed);
                break;
            case CarNavigationChange.EstimatesUpdated estimates:
                Estimates(map, estimates.Trip, estimates.Maneuver, state);
                break;
            case CarNavigationChange.AlertShown alert:
                ShowAlert(map, alert.Alert);
                break;
            case CarNavigationChange.SegmentChanged segment:
                CurrentSegment(segment.Index);
                break;
            case CarNavigationChange.Rerouted:
                Maneuvers(state.Maneuvers, state, resume: true);
                if (state.TripEstimates is { } tripEstimates)
                {
                    Estimates(map, tripEstimates, null, state);
                }

                break;
            case CarNavigationChange.Paused paused:
                _session?.PauseTrip(CarPlayNavigationModels.PauseReason(paused.Reason), paused.Description?.Text);
                break;
            case CarNavigationChange.Finished:
                _session?.FinishTrip();
                _session = null;
                break;
            case CarNavigationChange.Cancelled:
                if (_session is { } session)
                {
                    session.CancelTrip();
                    _session = null;
                }
                else
                {
                    map.HideTripPreviews();
                }

                break;
            case CarNavigationChange.EndedByCar:
                _session = null;
                break;
        }
    }

    // Full state on the (new) map template
    private void Show(CarNavigationSnapshot state)
    {
        if (_map is not { } map)
        {
            return;
        }

        switch (state.State)
        {
            case CarNavigationState.Previewing:
                ShowPreviews(map, state.Previews, state.SelectedPreview);
                break;
            case CarNavigationState.Active or CarNavigationState.Paused when state.Trip is { } trip:
                Start(map, trip, state);
                if (state.TripEstimates is { } estimates)
                {
                    Estimates(map, estimates, state.ManeuverEstimates, state);
                }

                if (state.State == CarNavigationState.Paused && state.PauseReason is { } reason)
                {
                    _session?.PauseTrip(CarPlayNavigationModels.PauseReason(reason), state.PauseDescription?.Text);
                }

                break;
        }
    }

    private void ShowPreviews(CPMapTemplate map, CarArray<CarTrip> trips, CarTrip? selected)
    {
        var natives = trips.Select(Trip).ToArray();
        var configuration = ((CarMapTemplate?)_mapState?.Model)?.StartTripTitle is { } start
            ? new CPTripPreviewTextConfiguration(start.Text, null, null)
            : null;
        var selectedNative = selected is null ? null : _trips[selected.Id];
        if (selectedNative is null)
        {
            map.ShowTripPreviews(natives, configuration);
        }
        else
        {
            map.ShowTripPreviews(natives, selectedNative, configuration);
        }

        // The estimate of each trip with its chosen route (after attaching: the driver's choice, otherwise the first
        // route)
        foreach (var trip in trips)
        {
            var route = _state.SelectedPreview?.Id == trip.Id && _state.SelectedRoute is { } chosen && trip.Routes.Any(r => r.Id == chosen.Id) ? chosen : trip.Routes[0];
            if (route.Estimates is { } estimates)
            {
                map.UpdateTravelEstimates(CarPlayNavigationModels.Estimates(estimates), _trips[trip.Id], CarPlayNavigationModels.Color(estimates.TimeRemainingColor));
            }
        }
    }

    private void Start(CPMapTemplate map, CarTrip trip, CarNavigationSnapshot state)
    {
        map.HideTripPreviews();
        _session = map.StartNavigationSession(Trip(trip));
        _addedManeuvers.Clear();
        Maneuvers(state.Maneuvers, state, resume: false);
        AddSegments(state);
        if (state.TripEstimates is { } estimates)
        {
            map.UpdateTravelEstimates(CarPlayNavigationModels.Estimates(estimates), _trips[trip.Id], CarPlayNavigationModels.Color(estimates.TimeRemainingColor));
        }
    }

    // Upcoming maneuvers; resume after a pause or detour: iOS 17.4 ResumeTrip(CPRouteInformation), below that only
    // the maneuvers
    private void Maneuvers(CarArray<CarManeuver> maneuvers, CarNavigationSnapshot state, bool resume)
    {
        if (_session is not { } session)
        {
            return;
        }

        var natives = maneuvers.Select(Maneuver).ToArray();
        _upcoming = natives;
        if (OperatingSystem.IsIOSVersionAtLeast(17, 4))
        {
            var guidances = new List<CPLaneGuidance>();
            var added = new List<CPManeuver>();
            var addedGuidances = new List<CPLaneGuidance>();
            foreach (var native in natives)
            {
                var isNew = _addedManeuvers.Add(native.UserInfo?.ToString() ?? string.Empty);
                if (isNew)
                {
                    added.Add(native);
                }

                if (native.LinkedLaneGuidance is { } guidance)
                {
                    guidances.Add(guidance);
                    if (isNew)
                    {
                        addedGuidances.Add(guidance);
                    }
                }
            }

            if (added.Count > 0)
            {
                session.AddManeuvers([.. added]);
                if (addedGuidances.Count > 0)
                {
                    session.AddLaneGuidances([.. addedGuidances]);
                }
            }

            // CPNavigationSession header: "Must be set to nil if there is no current lane guidance" (review M7)
            session.CurrentLaneGuidance = natives.Length > 0 ? natives[0].LinkedLaneGuidance : null;

            if (resume && !ResumeWithSegments(state, natives))
            {
                var tripEstimates = CarPlayNavigationModels.Estimates(state.TripEstimates ?? new CarTravelEstimates(new CarDistance(0, CarDistanceUnit.Meters), TimeSpan.Zero));
                var maneuverEstimates = CarPlayNavigationModels.Estimates(state.ManeuverEstimates ?? (maneuvers.IsEmpty ? null : maneuvers[0].InitialEstimates)
                    ?? new CarTravelEstimates(new CarDistance(0, CarDistanceUnit.Meters), TimeSpan.Zero));
                session.ResumeTrip(new CPRouteInformation(
                    natives, [.. guidances], natives.Take(1).ToArray(), (natives.Length > 0 ? natives[0].LinkedLaneGuidance : null) ?? new CPLaneGuidance(), tripEstimates, maneuverEstimates));
            }
        }

        session.UpcomingManeuvers = natives;
    }

    private void Estimates(CPMapTemplate map, CarTravelEstimates trip, CarTravelEstimates? maneuver, CarNavigationSnapshot state)
    {
        if (state.Trip is { } model && _trips.TryGetValue(model.Id, out var native))
        {
            map.UpdateTravelEstimates(CarPlayNavigationModels.Estimates(trip), native, CarPlayNavigationModels.Color(trip.TimeRemainingColor));
        }

        if (maneuver is not null && _session is { } session && _upcoming.FirstOrDefault() is { } next)
        {
            session.UpdateTravelEstimates(CarPlayNavigationModels.Estimates(maneuver), next);
        }
    }

    private void ShowAlert(CPMapTemplate map, CarNavigationAlert alert)
    {
        var native = new CPNavigationAlert(
            [.. alert.Title.Variants],
            alert.Subtitle is { } subtitle ? [.. subtitle.Variants] : [],
            Image("alert:" + alert.Id, alert.Image, _alertImageSize),
            AlertAction(alert, alert.PrimaryAction),
            alert.SecondaryAction is { } secondary ? AlertAction(alert, secondary) : null,
            alert.Duration.TotalSeconds);
        module.RememberAlert(native, alert.Id);
        map.PresentNavigationAlert(native, animated: true);
    }

    // CarPlay closes the alert after an action is selected; the session learns about it before the action handler
    private CPAlertAction AlertAction(CarNavigationAlert alert, CarAction action) =>
        new(action.Title.Text, CarPlayNavigationModels.Style(action.Style), _ =>
        {
            module.Runtime.Navigation.OnAlertDismissed(alert.Id);
            context.Run(action.OnSelected, context.Session.CreateContext(action), "Select");
        });

    private CPTrip Trip(CarTrip trip)
    {
        var routes = trip.Routes.Select(route => new CPRouteChoice(
            [.. route.Summary.Variants],
            route.AdditionalInformation is { } info ? [.. info.Variants] : [],
            route.SelectionSummary is { } selection ? [.. selection.Variants] : [])
        {
            UserInfo = new NSString(route.Id),
        }).ToArray();
        var native = NewTrip(trip, routes);
        native.UserInfo = new NSString(trip.Id);
#if IOS26_4_OR_GREATER
        // Stage 9: sharing the destination with the car's built-in navigation (iOS 26.1, binding 26.4)
        if (trip.IsDestinationShareable && OperatingSystem.IsIOSVersionAtLeast(26, 1))
        {
            native.HasShareableDestination = true;
        }
#endif
        if (trip.DestinationName is { } name && OperatingSystem.IsIOSVersionAtLeast(17, 4))
        {
            native.DestinationNameVariants = [.. name.Variants];
        }

        _trips[trip.Id] = native;
        return native;
    }

    // Stage 9 (iOS 26.4): route segments on the session; at start AddRouteSegments + the current segment
    private void AddSegments(CarNavigationSnapshot state)
    {
        if (_session is not { } session)
        {
            return;
        }

#if IOS26_4_OR_GREATER
        if (OperatingSystem.IsIOSVersionAtLeast(26, 4) && state.Trip is not null && state.Route is not null)
        {
            _segments = Segments(state, _upcoming);
            session.AddRouteSegments(_segments);
            session.CurrentSegment = _segments[Math.Clamp(state.SegmentIndex, 0, _segments.Length - 1)];
        }
#else
        _ = session;
#endif
    }

    private void CurrentSegment(int index)
    {
        if (_session is not { } session)
        {
            return;
        }

#if IOS26_4_OR_GREATER
        if (OperatingSystem.IsIOSVersionAtLeast(26, 4) && index >= 0 && index < _segments.Length)
        {
            session.CurrentSegment = _segments[index];
        }
#else
        _ = (session, index);
#endif
    }

    // iOS 26.4: detour and resume through resumeTripWithUpdatedRouteSegments (CPRouteInformation obsolete since 26.4)
    private bool ResumeWithSegments(CarNavigationSnapshot state, CPManeuver[] upcoming)
    {
        if (_session is not { } session)
        {
            return false;
        }

#if IOS26_4_OR_GREATER
        if (OperatingSystem.IsIOSVersionAtLeast(26, 4) && state.Trip is not null && state.Route is not null)
        {
            _segments = Segments(state, upcoming);
            var current = _segments[Math.Clamp(state.SegmentIndex, 0, _segments.Length - 1)];
            session.ResumeTrip(_segments, current, CarPlayNavigationModels.RerouteReason(state.RerouteReason ?? CarRerouteReason.Unknown));
            return true;
        }
#else
        _ = (session, state, upcoming);
#endif
        return false;
    }

#if IOS26_4_OR_GREATER
    // Segments from the model (without Segments: one segment start → destination). The current segment gets the
    // session's maneuvers, lanes and estimates, the others get their first maneuvers and the segment estimate; the path
    // (Path) is the coordinates shared with the car
    [System.Runtime.Versioning.SupportedOSPlatform("ios26.4")]
    [System.Runtime.Versioning.UnsupportedOSPlatform("maccatalyst")]
    private CPRouteSegment[] Segments(CarNavigationSnapshot state, CPManeuver[] upcoming)
    {
        var legs = CarNavigationSegments.Legs(state.Trip!, state.Route!);
        var zero = new CarTravelEstimates(new CarDistance(0, CarDistanceUnit.Meters), TimeSpan.Zero);
        return [.. legs.Select((leg, index) =>
        {
            var isCurrent = index == state.SegmentIndex;
            var maneuvers = leg.Maneuvers.Select(Maneuver).ToArray();
            var current = isCurrent && upcoming.Length > 0 ? upcoming : maneuvers.Take(1).ToArray();
            var tripEstimates = (isCurrent ? state.TripEstimates : null) ?? leg.Estimates ?? zero;
            var maneuverEstimates = (isCurrent ? state.ManeuverEstimates : null)
                ?? (isCurrent && !state.Maneuvers.IsEmpty ? state.Maneuvers[0].InitialEstimates : null)
                ?? (leg.Maneuvers.IsEmpty ? null : leg.Maneuvers[0].InitialEstimates)
                ?? zero;
            return CPRouteSegment.Create(
                CarPlayNavigationModels.Waypoint(leg.Origin),
                CarPlayNavigationModels.Waypoint(leg.Destination),
                maneuvers,
                [.. maneuvers.Select(m => m.LinkedLaneGuidance).OfType<CPLaneGuidance>()],
                current,
                (current.Length > 0 ? current[0].LinkedLaneGuidance : null) ?? new CPLaneGuidance(),
                CarPlayNavigationModels.Estimates(tripEstimates),
                CarPlayNavigationModels.Estimates(maneuverEstimates),
                [.. leg.Path.Select(CarPlayNavigationModels.Coordinate)]);
        })];
    }

    // Segment index from an object from CarPlay (map delegate; CPRouteSegment is NSCopying, so compare by Identifier)
    [System.Runtime.Versioning.SupportedOSPlatform("ios26.4")]
    [System.Runtime.Versioning.UnsupportedOSPlatform("maccatalyst")]
    internal int? SegmentIndex(CPRouteSegment? segment)
    {
        if (segment is null)
        {
            return null;
        }

        var index = Array.FindIndex(_segments, native => native.Handle == segment.Handle || native.Identifier.Equals(segment.Identifier));
        return index >= 0 ? index : null;
    }
#endif

    // Stage 9: from iOS 26.4 a CPTrip with CPNavigationWaypoint (the MKMapItem constructor is obsolete since 26.4);
    // asset 26.0 and iOS < 26.4: MKMapItem as in Stage 8
    private static CPTrip NewTrip(CarTrip trip, CPRouteChoice[] routes)
    {
#if IOS26_4_OR_GREATER
        if (OperatingSystem.IsIOSVersionAtLeast(26, 4))
        {
            return new CPTrip(
                CarPlayNavigationModels.Waypoint(new CarWaypoint(trip.Origin)),
                CarPlayNavigationModels.Waypoint(new CarWaypoint(trip.Destination) { Name = trip.DestinationName, Address = trip.DestinationAddress?.Text }),
                routes);
        }

#pragma warning disable CA1422 // CPTrip(MKMapItem…) is obsolete since iOS 26.4; this is the path for iOS 15–26.3
        return new CPTrip(CarPlayNavigationModels.MapItem(trip.Origin, null), CarPlayNavigationModels.MapItem(trip.Destination, trip.DestinationName?.Text), routes);
#pragma warning restore CA1422
#else
        return new CPTrip(CarPlayNavigationModels.MapItem(trip.Origin, null), CarPlayNavigationModels.MapItem(trip.Destination, trip.DestinationName?.Text), routes);
#endif
    }

    private CPManeuver Maneuver(CarManeuver model)
    {
        CPManeuver? native = null;
        native = new CPManeuver
        {
            InstructionVariants = [.. model.Instruction.Variants],
            UserInfo = new NSString(model.Id),
        };
        if (Image("symbol:" + model.Id, model.Icon, _symbolSize, loaded => native.SymbolImage = loaded) is { } symbol)
        {
            native.SymbolImage = symbol;
        }

        if (Image("junction:" + model.Id, model.JunctionImage, _junctionSize, loaded => native.JunctionImage = loaded) is { } junction)
        {
            native.JunctionImage = junction;
        }

        if (model.InitialEstimates is { } estimates)
        {
            native.InitialTravelEstimates = CarPlayNavigationModels.Estimates(estimates);
        }

        if (OperatingSystem.IsIOSVersionAtLeast(17, 4))
        {
            native.ManeuverType = CarPlayNavigationModels.ManeuverType(model);
            native.TrafficSide = model.TrafficSide == CarTrafficSide.Left ? CPTrafficSide.Left : CPTrafficSide.Right;
            if (model.Road is { } road)
            {
                native.RoadFollowingManeuverVariants = [.. road.Variants];
            }

            if (!model.Lanes.IsEmpty)
            {
                native.LinkedLaneGuidance = new CPLaneGuidance
                {
                    InstructionVariants = [.. model.Instruction.Variants],
                    Lanes = [.. model.Lanes.Select(CarPlayNavigationModels.Lane)],
                };
            }
        }

        return native;
    }

    private UIImage? Image(string key, CarImage? image, CGSize size, Action<UIImage>? later = null) =>
        _mapState is { } state && image is not null
            ? context.Image(state, key, image, size, later ?? (_ => { }))
            : null;
}

// Navigation model → CarPlay objects without state
internal static class CarPlayNavigationModels
{
    public static CPTravelEstimates Estimates(CarTravelEstimates estimates) =>
        new(new NSMeasurement<NSUnitLength>(estimates.DistanceRemaining.Value, Unit(estimates.DistanceRemaining.Unit)), estimates.TimeRemaining.TotalSeconds);

    public static NSUnitLength Unit(CarDistanceUnit unit) => unit switch
    {
        CarDistanceUnit.Kilometers => NSUnitLength.Kilometers,
        CarDistanceUnit.Feet => NSUnitLength.Feet,
        CarDistanceUnit.Yards => NSUnitLength.Yards,
        CarDistanceUnit.Miles => NSUnitLength.Miles,
        _ => NSUnitLength.Meters,
    };

    public static CPTimeRemainingColor Color(CarTravelEstimatesColor color) => color switch
    {
        CarTravelEstimatesColor.Green => CPTimeRemainingColor.Green,
        CarTravelEstimatesColor.Orange => CPTimeRemainingColor.Orange,
        CarTravelEstimatesColor.Red => CPTimeRemainingColor.Red,
        _ => CPTimeRemainingColor.Default,
    };

    public static CPTripPauseReason PauseReason(CarNavigationPauseReason reason) => reason switch
    {
        CarNavigationPauseReason.Arrived => CPTripPauseReason.Arrived,
        CarNavigationPauseReason.Loading => CPTripPauseReason.Loading,
        CarNavigationPauseReason.Locating => CPTripPauseReason.Locating,
        CarNavigationPauseReason.Rerouting => CPTripPauseReason.Rerouting,
        _ => CPTripPauseReason.ProceedToRoute,
    };

    public static CPAlertActionStyle Style(CarActionStyle style) => style switch
    {
        CarActionStyle.Cancel => CPAlertActionStyle.Cancel,
        CarActionStyle.Destructive => CPAlertActionStyle.Destructive,
        _ => CPAlertActionStyle.Default,
    };

    // iOS 17.4: maneuver type (CarPlay draws the symbol when the app does not supply one); roundabout with an exit
    // number → RoundaboutExitN
    [System.Runtime.Versioning.SupportedOSPlatform("ios17.4")]
    [System.Runtime.Versioning.UnsupportedOSPlatform("maccatalyst")]
    public static CPManeuverType ManeuverType(CarManeuver maneuver) => maneuver.Kind switch
    {
        CarManeuverKind.Depart => CPManeuverType.StartRoute,
        CarManeuverKind.Straight => CPManeuverType.StraightAhead,
        CarManeuverKind.SlightLeft => CPManeuverType.SlightLeftTurn,
        CarManeuverKind.Left => CPManeuverType.LeftTurn,
        CarManeuverKind.SharpLeft => CPManeuverType.SharpLeftTurn,
        CarManeuverKind.SlightRight => CPManeuverType.SlightRightTurn,
        CarManeuverKind.Right => CPManeuverType.RightTurn,
        CarManeuverKind.SharpRight => CPManeuverType.SharpRightTurn,
        CarManeuverKind.UTurnLeft or CarManeuverKind.UTurnRight => CPManeuverType.UTurn,
        CarManeuverKind.KeepLeft or CarManeuverKind.MergeLeft or CarManeuverKind.ForkLeft => CPManeuverType.KeepLeft,
        CarManeuverKind.KeepRight or CarManeuverKind.MergeRight or CarManeuverKind.ForkRight => CPManeuverType.KeepRight,
        CarManeuverKind.OnRampLeft or CarManeuverKind.OnRampRight => CPManeuverType.OnRamp,
        CarManeuverKind.OffRampLeft => CPManeuverType.HighwayOffRampLeft,
        CarManeuverKind.OffRampRight => CPManeuverType.HighwayOffRampRight,
        CarManeuverKind.RoundaboutEnter => CPManeuverType.EnterRoundabout,
        CarManeuverKind.RoundaboutExit => CPManeuverType.ExitRoundabout,
        CarManeuverKind.Roundabout when maneuver.RoundaboutExitNumber is { } exit =>
            (CPManeuverType)((ulong)CPManeuverType.RoundaboutExit1 + (ulong)(exit - 1)),
        CarManeuverKind.Roundabout => CPManeuverType.EnterRoundabout,
        CarManeuverKind.Ferry => CPManeuverType.EnterFerry,
        CarManeuverKind.Destination => CPManeuverType.ArriveAtDestination,
        CarManeuverKind.DestinationLeft => CPManeuverType.ArriveAtDestinationLeft,
        CarManeuverKind.DestinationRight => CPManeuverType.ArriveAtDestinationRight,
        CarManeuverKind.NameChange => CPManeuverType.FollowRoad,
        _ => CPManeuverType.NoTurn,
    };

    // Lane shape angles. iOS 18: a lane with a recommendation: initWithAngles:highlightedAngle:isPreferred: through
    // objc_msgSend, because the binding types highlightedAngle as an array (Objective-C: a single NSMeasurement;
    // Stage 8 ruling after review I5); a lane without a recommendation: CPLane(angles) (status NotGood); iOS
    // 17.4–17.x: the PrimaryAngle/SecondaryAngles/Status properties, obsolete since iOS 18 (recommended primary
    // direction, Preferred/NotGood)
    [System.Runtime.Versioning.SupportedOSPlatform("ios17.4")]
    [System.Runtime.Versioning.UnsupportedOSPlatform("maccatalyst")]
    public static CPLane Lane(CarLane lane)
    {
        var recommended = lane.Directions.Where(direction => direction.IsRecommended).ToArray();
        var primary = recommended.Length > 0 ? recommended[0] : lane.Directions[0];
        if (OperatingSystem.IsIOSVersionAtLeast(18))
        {
            var angles = lane.Directions.Select(direction => Angle(direction.Shape)).ToArray();
            return recommended.Length > 0 ? ObjCMessages.Lane(angles, Angle(primary.Shape), preferred: true) : new CPLane(angles);
        }

#pragma warning disable CA1422 // PrimaryAngle/SecondaryAngles/Status obsolete since iOS 18; this is the path for iOS 17.4–17.x
        return new CPLane
        {
            PrimaryAngle = Angle(primary.Shape),
            SecondaryAngles = [.. lane.Directions.Where(direction => direction != primary).Select(direction => Angle(direction.Shape))],
            Status = recommended.Length > 0 ? CPLaneStatus.Preferred : CPLaneStatus.NotGood,
        };
#pragma warning restore CA1422
    }

    public static MKMapItem MapItem(CarLocation location, string? name)
    {
        var item = OperatingSystem.IsIOSVersionAtLeast(26)
            ? new MKMapItem(new CLLocation(location.Latitude, location.Longitude), null)
            : LegacyMapItem(location);
        if (name is not null)
        {
            item.Name = name;
        }

        return item;
    }

#if IOS26_4_OR_GREATER
    // Stage 9: iOS 26.4 waypoint (center, name, address; without distance threshold, entrances and time zone)
    [System.Runtime.Versioning.SupportedOSPlatform("ios26.4")]
    [System.Runtime.Versioning.UnsupportedOSPlatform("maccatalyst")]
    public static CPNavigationWaypoint Waypoint(CarWaypoint waypoint) =>
        CPNavigationWaypoint.Create(Coordinate(waypoint.Location), null!, waypoint.Name?.Text!, waypoint.Address!, [], null!);

    [System.Runtime.Versioning.SupportedOSPlatform("ios26.4")]
    [System.Runtime.Versioning.UnsupportedOSPlatform("maccatalyst")]
    public static CPLocationCoordinate3D Coordinate(CarLocation location) =>
        new() { Latitude = location.Latitude, Longitude = location.Longitude, Altitude = 0 };

    // A waypoint from the car (a stop request or destination) → model
    [System.Runtime.Versioning.SupportedOSPlatform("ios26.4")]
    [System.Runtime.Versioning.UnsupportedOSPlatform("maccatalyst")]
    public static CarWaypoint FromNative(CPNavigationWaypoint waypoint) =>
        new(new CarLocation(waypoint.CenterPoint.Latitude, waypoint.CenterPoint.Longitude))
        {
            Name = string.IsNullOrEmpty(waypoint.Name) ? null : new CarText(waypoint.Name),
            Address = string.IsNullOrEmpty(waypoint.Address) ? null : waypoint.Address,
        };

    [System.Runtime.Versioning.SupportedOSPlatform("ios26.4")]
    [System.Runtime.Versioning.UnsupportedOSPlatform("maccatalyst")]
    public static CPRerouteReason RerouteReason(CarRerouteReason reason) => reason switch
    {
        CarRerouteReason.MissedTurn => CPRerouteReason.MissedTurn,
        CarRerouteReason.Offline => CPRerouteReason.Offline,
        CarRerouteReason.AlternateRoute => CPRerouteReason.AlternateRoute,
        CarRerouteReason.WaypointModified => CPRerouteReason.WaypointModified,
        CarRerouteReason.Mandated => CPRerouteReason.Mandated,
        _ => CPRerouteReason.Unknown,
    };

    [System.Runtime.Versioning.SupportedOSPlatform("ios26.4")]
    [System.Runtime.Versioning.UnsupportedOSPlatform("maccatalyst")]
    public static CarRouteSource RouteSource(CPRouteSource source) => source switch
    {
        CPRouteSource.IOSUnchanged => CarRouteSource.PhoneUnchanged,
        CPRouteSource.IOSRouteModified => CarRouteSource.PhoneRouteModified,
        CPRouteSource.IOSRouteDestinationsModified => CarRouteSource.PhoneRouteAndDestinationsModified,
        CPRouteSource.IOSDestinationsOnly => CarRouteSource.PhoneDestinationsOnly,
        CPRouteSource.Vehicle => CarRouteSource.Vehicle,
        _ => CarRouteSource.Inactive,
    };

#endif
    private static NSMeasurement<NSUnitAngle> Angle(CarLaneShape shape) => new(CarNavigationTemplates.LaneAngle(shape), NSUnitAngle.Degrees);

#pragma warning disable CA1422 // MKMapItem(MKPlacemark) is obsolete since iOS 26; this is the path for iOS 15–25 (like POI from Stage 7)
    private static MKMapItem LegacyMapItem(CarLocation location) =>
        new(new MKPlacemark(new CLLocationCoordinate2D(location.Latitude, location.Longitude)));
#pragma warning restore CA1422
}
