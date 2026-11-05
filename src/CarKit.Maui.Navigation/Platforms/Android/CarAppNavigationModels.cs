using AndroidX.Car.App.Model;
using AndroidX.Car.App.Navigation.Model;
using CarKit.Maui.Renderers;
using NativeCarColor = AndroidX.Car.App.Model.CarColor;
using NativeDistance = AndroidX.Car.App.Model.Distance;

namespace CarKit.Maui.Navigation;

// Step icons: the car screen through CarAppRenderContext.Icon (rebuild after loading), the cluster through its own
// loader
internal sealed class CarAppIcons(Func<CarImage?, int, CarIcon?> load, Microsoft.Extensions.Logging.ILogger logger)
{
    public Microsoft.Extensions.Logging.ILogger Logger { get; } = logger;

    public static CarAppIcons For(CarAppRenderContext context, CarAppScreenState screen) =>
        new((image, pixels) => context.Icon(screen, image, pixels), context.Logger);

    public CarIcon? Icon(CarImage? image, int pixels) => load(image, pixels);
}

// Navigation model → Car App Library objects (Step, Maneuver, Lane, TravelEstimate, Trip, Destination); no screen state
// apart from images (icons loaded in the background rebuild the screen)
internal static class CarAppNavigationModels
{
    public static NativeDistance Distance(CarDistance distance)
    {
        var (value, unit) = CarDistanceUnits.AndroidAuto(distance);
        return NativeDistance.Create(value, unit)!;
    }

    public static TravelEstimate Estimate(CarTravelEstimates estimates, DateTimeOffset now)
    {
        var arrival = CarNavigationTemplates.ArrivalTime(estimates, now);
        var builder = new TravelEstimate.Builder(
            Distance(estimates.DistanceRemaining),
            DateTimeWithZone.Create(arrival.ToUnixTimeMilliseconds(), (int)arrival.Offset.TotalSeconds, Java.Util.TimeZone.Default!.ID!)!)
            .SetRemainingTimeSeconds((long)estimates.TimeRemaining.TotalSeconds)!;
        if (Color(estimates.TimeRemainingColor) is { } color)
        {
            builder.SetRemainingTimeColor(color);
        }

        return builder.Build()!;
    }

    public static NativeCarColor? Color(CarTravelEstimatesColor color) => color switch
    {
        CarTravelEstimatesColor.Green => NativeCarColor.Green,
        CarTravelEstimatesColor.Orange => NativeCarColor.Yellow,
        CarTravelEstimatesColor.Red => NativeCarColor.Red,
        _ => null,
    };

    // Step: instruction, maneuver (type, icon, roundabout), road; lanes only with a lanes image (RoutingInfo: "Current
    // step must have a lanes image if the lane information is set"), otherwise skipped with a warning once per maneuver
    public static Step Step(CarManeuver model, CarAppIcons icons, HashSet<string> lanesWarned)
    {
        var builder = new Step.Builder(CarAppTemplateRenderers.Text(model.Instruction))
            .SetManeuver(Maneuver(model, icons))!;
        if (model.Road is { } road)
        {
            builder.SetRoad(road.Text);
        }

        if (!model.Lanes.IsEmpty)
        {
            if (icons.Icon(model.LanesImage, CarAppImages.LargePixels) is { } lanesImage)
            {
                foreach (var lane in model.Lanes)
                {
                    builder.AddLane(Lane(lane));
                }

                builder.SetLanesImage(lanesImage);
            }
            else if (model.LanesImage is null && lanesWarned.Add(model.Id))
            {
                CarNavigationLog.LanesWithoutImage(icons.Logger, model.Id);
            }
        }

        return builder.Build()!;
    }

    public static Maneuver Maneuver(CarManeuver model, CarAppIcons icons)
    {
        var builder = new Maneuver.Builder(ManeuverType(model));
        if (RequiresExitNumber(model))
        {
            builder.SetRoundaboutExitNumber(model.RoundaboutExitNumber!.Value);
        }

        if (model is { Kind: CarManeuverKind.Roundabout, RoundaboutExitAngle: { } angle })
        {
            builder.SetRoundaboutExitAngle(angle);
        }

        if (icons.Icon(model.Icon, CarAppImages.SmallPixels) is { } icon)
        {
            builder.SetIcon(icon);
        }

        return builder.Build()!;
    }

    // Roundabout with an exit number: TYPE_ROUNDABOUT_ENTER_AND_EXIT_* (with an angle: *_WITH_ANGLE); the roundabout
    // direction from right-hand or left-hand traffic
    public static int ManeuverType(CarManeuver model)
    {
        var clockwise = model.TrafficSide == CarTrafficSide.Left;
        return model.Kind switch
        {
            CarManeuverKind.Depart => AndroidX.Car.App.Navigation.Model.Maneuver.TypeDepart,
            CarManeuverKind.Straight => AndroidX.Car.App.Navigation.Model.Maneuver.TypeStraight,
            CarManeuverKind.SlightLeft => AndroidX.Car.App.Navigation.Model.Maneuver.TypeTurnSlightLeft,
            CarManeuverKind.Left => AndroidX.Car.App.Navigation.Model.Maneuver.TypeTurnNormalLeft,
            CarManeuverKind.SharpLeft => AndroidX.Car.App.Navigation.Model.Maneuver.TypeTurnSharpLeft,
            CarManeuverKind.SlightRight => AndroidX.Car.App.Navigation.Model.Maneuver.TypeTurnSlightRight,
            CarManeuverKind.Right => AndroidX.Car.App.Navigation.Model.Maneuver.TypeTurnNormalRight,
            CarManeuverKind.SharpRight => AndroidX.Car.App.Navigation.Model.Maneuver.TypeTurnSharpRight,
            CarManeuverKind.UTurnLeft => AndroidX.Car.App.Navigation.Model.Maneuver.TypeUTurnLeft,
            CarManeuverKind.UTurnRight => AndroidX.Car.App.Navigation.Model.Maneuver.TypeUTurnRight,
            CarManeuverKind.KeepLeft => AndroidX.Car.App.Navigation.Model.Maneuver.TypeKeepLeft,
            CarManeuverKind.KeepRight => AndroidX.Car.App.Navigation.Model.Maneuver.TypeKeepRight,
            CarManeuverKind.MergeLeft => AndroidX.Car.App.Navigation.Model.Maneuver.TypeMergeLeft,
            CarManeuverKind.MergeRight => AndroidX.Car.App.Navigation.Model.Maneuver.TypeMergeRight,
            CarManeuverKind.OnRampLeft => AndroidX.Car.App.Navigation.Model.Maneuver.TypeOnRampNormalLeft,
            CarManeuverKind.OnRampRight => AndroidX.Car.App.Navigation.Model.Maneuver.TypeOnRampNormalRight,
            CarManeuverKind.OffRampLeft => AndroidX.Car.App.Navigation.Model.Maneuver.TypeOffRampNormalLeft,
            CarManeuverKind.OffRampRight => AndroidX.Car.App.Navigation.Model.Maneuver.TypeOffRampNormalRight,
            CarManeuverKind.ForkLeft => AndroidX.Car.App.Navigation.Model.Maneuver.TypeForkLeft,
            CarManeuverKind.ForkRight => AndroidX.Car.App.Navigation.Model.Maneuver.TypeForkRight,
            CarManeuverKind.RoundaboutEnter => clockwise ? AndroidX.Car.App.Navigation.Model.Maneuver.TypeRoundaboutEnterCw : AndroidX.Car.App.Navigation.Model.Maneuver.TypeRoundaboutEnterCcw,
            CarManeuverKind.RoundaboutExit => clockwise ? AndroidX.Car.App.Navigation.Model.Maneuver.TypeRoundaboutExitCw : AndroidX.Car.App.Navigation.Model.Maneuver.TypeRoundaboutExitCcw,
            CarManeuverKind.Roundabout when model.RoundaboutExitAngle is not null => clockwise
                ? AndroidX.Car.App.Navigation.Model.Maneuver.TypeRoundaboutEnterAndExitCwWithAngle
                : AndroidX.Car.App.Navigation.Model.Maneuver.TypeRoundaboutEnterAndExitCcwWithAngle,
            CarManeuverKind.Roundabout => clockwise
                ? AndroidX.Car.App.Navigation.Model.Maneuver.TypeRoundaboutEnterAndExitCw
                : AndroidX.Car.App.Navigation.Model.Maneuver.TypeRoundaboutEnterAndExitCcw,
            CarManeuverKind.Ferry => AndroidX.Car.App.Navigation.Model.Maneuver.TypeFerryBoat,
            CarManeuverKind.Destination => AndroidX.Car.App.Navigation.Model.Maneuver.TypeDestination,
            CarManeuverKind.DestinationLeft => AndroidX.Car.App.Navigation.Model.Maneuver.TypeDestinationLeft,
            CarManeuverKind.DestinationRight => AndroidX.Car.App.Navigation.Model.Maneuver.TypeDestinationRight,
            CarManeuverKind.NameChange => AndroidX.Car.App.Navigation.Model.Maneuver.TypeNameChange,
            _ => AndroidX.Car.App.Navigation.Model.Maneuver.TypeUnknown,
        };
    }

    public static Lane Lane(CarLane model)
    {
        var builder = new Lane.Builder();
        foreach (var direction in model.Directions)
        {
            builder.AddDirection(LaneDirection.Create(Shape(direction.Shape), direction.IsRecommended)!);
        }

        return builder.Build()!;
    }

    public static int Shape(CarLaneShape shape) => shape switch
    {
        CarLaneShape.Straight => LaneDirection.ShapeStraight,
        CarLaneShape.SlightLeft => LaneDirection.ShapeSlightLeft,
        CarLaneShape.Left => LaneDirection.ShapeNormalLeft,
        CarLaneShape.SharpLeft => LaneDirection.ShapeSharpLeft,
        CarLaneShape.UTurnLeft => LaneDirection.ShapeUTurnLeft,
        CarLaneShape.SlightRight => LaneDirection.ShapeSlightRight,
        CarLaneShape.Right => LaneDirection.ShapeNormalRight,
        CarLaneShape.SharpRight => LaneDirection.ShapeSharpRight,
        CarLaneShape.UTurnRight => LaneDirection.ShapeUTurnRight,
        _ => LaneDirection.ShapeUnknown,
    };

    // NavigationManager.updateTrip: destination with an estimate, steps with estimates (the first: the maneuver
    // estimate), road, loading while recalculating
    public static Trip Trip(CarNavigationSnapshot state, CarAppIcons icons, HashSet<string> lanesWarned, DateTimeOffset now)
    {
        var builder = new Trip.Builder();
        var loading = CarNavigationTemplates.InfoKind(state) == CarNavigationInfoKind.Loading;
        builder.SetLoading(loading);
        if (loading)
        {
            return builder.Build()!;
        }

        // Stage 9: the destination of every remaining leg (stops of a multi-waypoint route) with its estimate; the
        // route destination with the session estimate
        if (state.Trip is { } trip && state.Route is { } route)
        {
            var stops = CarNavigationSegments.RemainingStops(trip, route, state.SegmentIndex, state.TripEstimates);
            for (var i = 0; i < stops.Count; i++)
            {
                var (waypoint, estimates) = stops[i];
                var destination = new Destination.Builder();
                var last = i == stops.Count - 1;
                destination.SetName(waypoint.Name?.Text ?? (last ? trip.DestinationName?.Text ?? route.Summary.Text : null) ?? " ");
                if (waypoint.Address is { } address)
                {
                    destination.SetAddress(address);
                }

                builder.AddDestination(destination.Build()!, Estimate(estimates, now));
            }
        }

        for (var i = 0; i < state.Maneuvers.Count; i++)
        {
            var maneuver = state.Maneuvers[i];
            var estimates = i == 0 ? state.ManeuverEstimates ?? maneuver.InitialEstimates : maneuver.InitialEstimates;
            if (estimates is null)
            {
                continue;
            }

            builder.AddStep(Step(maneuver, icons, lanesWarned), Estimate(estimates, now));
        }

        return builder.Build()!;
    }

    private static bool RequiresExitNumber(CarManeuver model) => model is { Kind: CarManeuverKind.Roundabout, RoundaboutExitNumber: not null };
}
