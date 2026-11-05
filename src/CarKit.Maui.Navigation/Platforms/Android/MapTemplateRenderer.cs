using Android.Text;
using AndroidX.Car.App.Model;
using AndroidX.Car.App.Navigation.Model;
using CarKit.Maui.Renderers;
using NativeAction = AndroidX.Car.App.Model.Action;
using NativeCarColor = AndroidX.Car.App.Model.CarColor;

namespace CarKit.Maui.Navigation;

// CarMapTemplate → Car App Library template according to the navigation state (spec: "Navigation module decisions
// (Stage 8)", CarMapTemplate on Android Auto): trip preview → MapWithContentTemplate (API 7) or
// RoutePreviewNavigationTemplate (below that), other states → NavigationTemplate (RoutingInfo, MessageInfo, destination
// estimate). ConfigureAndroidAuto hooks before Build(). Main thread.
internal static class MapTemplateRenderer
{
    // ACTIONS_CONSTRAINTS_NAVIGATION and ACTIONS_CONSTRAINTS_MAP: at most 4 actions
    private const int MaxActions = 4;

    public static CarAppRendered Render(
        CarMapTemplate model, CarAppRenderContext context, CarAppScreenState screen, CarNavigationSnapshot state, CarAppNavigationConnection connection) =>
        state.State == CarNavigationState.Previewing
            ? context.ApiLevel >= CarNavigationTemplates.MapWithContentApiLevel
                ? RoutePreviews(model, context, screen, state, connection)
                : LegacyRoutePreviews(model, context, screen, state, connection)
            : Navigation(model, context, screen, state, connection);

    private static CarAppRendered Navigation(
        CarMapTemplate model, CarAppRenderContext context, CarAppScreenState screen, CarNavigationSnapshot state, CarAppNavigationConnection connection)
    {
        var builder = new NavigationTemplate.Builder();
        if (ActionStrip(model, context, screen) is { } strip)
        {
            builder.SetActionStrip(strip);
        }

        if (MapActionStrip(model, context, screen) is { } mapStrip)
        {
            builder.SetMapActionStrip(mapStrip);
        }

        if (model.IsPanningEnabled)
        {
            builder.SetPanModeListener(new PanModeListener(connection));
        }

        if (model.GuidanceBackgroundColor is { } color)
        {
            var argb = (color.Alpha << 24) | (color.Red << 16) | (color.Green << 8) | color.Blue;
            builder.SetBackgroundColor(NativeCarColor.CreateCustom(argb, argb)!);
        }

        var now = DateTimeOffset.Now;
        ApplyNavigationInfo(builder, state, CarAppIcons.For(context, screen), connection.LanesWarned, now);

        CarAppHooks.Apply(model, builder, context);
        return new(builder.Build()!, new CarAppTemplateShape(CarAppTemplateTypes.Navigation, null, IsLoading: false));
    }

    // Navigation info (car screen and cluster): RoutingInfo with a step and distance (required except while loading),
    // the next step and a junction, RoutingInfo while loading or MessageInfo; destination estimate during guidance
    public static void ApplyNavigationInfo(NavigationTemplate.Builder builder, CarNavigationSnapshot state, CarAppIcons icons, HashSet<string> lanesWarned, DateTimeOffset now)
    {
        switch (CarNavigationTemplates.InfoKind(state))
        {
            case CarNavigationInfoKind.Routing:
                var current = state.Maneuvers[0];
                var distance = CarNavigationTemplates.DistanceToNext(state) ?? new CarDistance(0, CarDistanceUnit.Meters);
                var routing = new RoutingInfo.Builder()
                    .SetCurrentStep(CarAppNavigationModels.Step(current, icons, lanesWarned), CarAppNavigationModels.Distance(distance))!;
                if (state.Maneuvers.Count > 1)
                {
                    routing.SetNextStep(CarAppNavigationModels.Step(state.Maneuvers[1], icons, lanesWarned));
                }

                if (icons.Icon(current.JunctionImage, CarAppImages.LargePixels) is { } junction)
                {
                    routing.SetJunctionImage(junction);
                }

                builder.SetNavigationInfo(routing.Build()!);
                break;
            case CarNavigationInfoKind.Loading:
                builder.SetNavigationInfo(new RoutingInfo.Builder().SetLoading(true)!.Build()!);
                break;
            case CarNavigationInfoKind.Message:
                builder.SetNavigationInfo(new MessageInfo.Builder(CarAppTemplateRenderers.Text(CarNavigationTemplates.MessageTitle(state))).Build()!);
                break;
        }

        if (state.State is CarNavigationState.Active or CarNavigationState.Paused && state.TripEstimates is { } estimates)
        {
            builder.SetDestinationTravelEstimate(CarAppNavigationModels.Estimate(estimates, now));
        }
    }

    // Trip preview (API 7): MapWithContentTemplate with a MapController and a route choice list; the Start action in
    // the list action strip
    private static CarAppRendered RoutePreviews(
        CarMapTemplate model, CarAppRenderContext context, CarAppScreenState screen, CarNavigationSnapshot state, CarAppNavigationConnection connection)
    {
        var (list, rows, title) = RouteList(model, context, state, connection);
        var content = new ListTemplate.Builder().SetSingleList(list)!;
        content.SetHeader(new Header.Builder().SetTitle(title)!.SetStartHeaderAction(HeaderRenderer.Start(screen))!.Build()!);
        // An action with a title: Header end actions require icons (ACTIONS_CONSTRAINTS_MULTI_HEADER), and the list
        // ActionStrip accepts one action with a title (ACTIONS_CONSTRAINTS_SIMPLE)
#pragma warning disable CS0618 // ListTemplate.Builder.SetActionStrip is obsolete since Car App 1.7 (the Header replacement requires an icon)
        content.SetActionStrip(new ActionStrip.Builder().AddAction(StartAction(model, context, state, connection))!.Build()!);
#pragma warning restore CS0618
        var controller = new MapController.Builder();
        if (MapActionStrip(model, context, screen) is { } mapStrip)
        {
            controller.SetMapActionStrip(mapStrip);
        }

        if (model.IsPanningEnabled)
        {
            controller.SetPanModeListener(new PanModeListener(connection));
        }

        var builder = new MapWithContentTemplate.Builder()
            .SetContentTemplate(content.Build()!)!
            .SetMapController(controller.Build()!)!;
        CarAppHooks.Apply(model, builder, context);
        return new(builder.Build()!, new CarAppTemplateShape(CarAppTemplateTypes.MapWithContent, title, IsLoading: false) { Sections = [new CarAppSectionShape(null, rows)] });
    }

    // Car App API < 7: the obsolete RoutePreviewNavigationTemplate (choice list, navigation action with a title)
    private static CarAppRendered LegacyRoutePreviews(
        CarMapTemplate model, CarAppRenderContext context, CarAppScreenState screen, CarNavigationSnapshot state, CarAppNavigationConnection connection)
    {
        var (list, rows, title) = RouteList(model, context, state, connection);
#pragma warning disable CS0618 // RoutePreviewNavigationTemplate is obsolete since Car App 1.7 (MapWithContentTemplate requires API 7); path for hosts < 7
        var builder = new RoutePreviewNavigationTemplate.Builder()
            .SetTitle(title)!
            .SetHeaderAction(HeaderRenderer.Start(screen))!
            .SetItemList(list)!
            .SetNavigateAction(StartAction(model, context, state, connection))!;
        if (MapActionStrip(model, context, screen) is { } mapStrip)
        {
            builder.SetMapActionStrip(mapStrip);
        }

        if (ActionStrip(model, context, screen) is { } strip)
        {
            builder.SetActionStrip(strip);
        }

        if (model.IsPanningEnabled)
        {
            builder.SetPanModeListener(new PanModeListener(connection));
        }

        CarAppHooks.Apply(model, builder, context);
        var template = builder.Build()!;
#pragma warning restore CS0618
        return new(template, new CarAppTemplateShape(CarAppTemplateTypes.RoutePreview, title, IsLoading: false) { Sections = [new CarAppSectionShape(null, rows)] });
    }

    // Row of a route of the selected trip: summary, extra info, estimate (DistanceSpan + DurationSpan); selection →
    // session
    private static (ItemList List, CarArray<CarAppRowShape> Rows, string Title) RouteList(
        CarMapTemplate model, CarAppRenderContext context, CarNavigationSnapshot state, CarAppNavigationConnection connection)
    {
        var trip = state.SelectedPreview ?? state.Previews[0];
        var selected = state.SelectedRoute is { } route && trip.Routes.Any(r => r.Id == route.Id) ? route : trip.Routes[0];
        var list = new ItemList.Builder();
        var rows = new List<CarAppRowShape>();
        foreach (var choice in trip.Routes)
        {
            var row = new Row.Builder().SetTitle(CarAppTemplateRenderers.Text(choice.Summary))!;
            if (choice.Estimates is { } estimates)
            {
                row.AddText(EstimateText(estimates));
            }

            if (choice.AdditionalInformation is { } info)
            {
                row.AddText(CarAppTemplateRenderers.Text(info));
            }

            list.AddItem(row.Build()!);
            rows.Add(new CarAppRowShape(RowRenderer.Title(choice.Summary)));
        }

        list.SetSelectedIndex(trip.Routes.ToList().FindIndex(r => r.Id == selected.Id));
        list.SetOnSelectedListener(new RouteSelectedListener(connection, trip));
        return (list.Build()!, [.. rows], trip.DestinationName?.Text ?? model.Title?.Text ?? " ");
    }

    // "12 km · 25 min" with spans (the host formats units and time)
    private static SpannableString EstimateText(CarTravelEstimates estimates)
    {
        var text = new SpannableString("  ·  ");
        text.SetSpan(DistanceSpan.Create(CarAppNavigationModels.Distance(estimates.DistanceRemaining)), 0, 1, SpanTypes.InclusiveInclusive);
        text.SetSpan(DurationSpan.Create((long)estimates.TimeRemaining.TotalSeconds), 4, 5, SpanTypes.InclusiveInclusive);
        return text;
    }

    private static NativeAction StartAction(CarMapTemplate model, CarAppRenderContext context, CarNavigationSnapshot state, CarAppNavigationConnection connection) =>
        new NativeAction.Builder()
            .SetTitle(CarAppTemplateRenderers.Text(model.StartTripTitle ?? "Start"))!
            .SetOnClickListener(new StartListener(connection))!
            .Build()!;

    // Action strip (required by NavigationTemplate; the validator guards TrailingBarButtons): at most 4, titles and
    // icons
    private static ActionStrip? ActionStrip(CarMapTemplate model, CarAppRenderContext context, CarAppScreenState screen)
    {
        var strip = new ActionStrip.Builder();
        var count = 0;
        foreach (var button in model.TrailingBarButtons.Take(MaxActions))
        {
            var icon = context.Icon(screen, button.Image, CarAppImages.SmallPixels);
            if (button.Title is null && icon is null)
            {
                continue;
            }

            var action = new NativeAction.Builder().SetOnClickListener(context.Click(button, button.OnSelected))!;
            if (button.Title is { } title)
            {
                action.SetTitle(CarAppTemplateRenderers.Text(title));
            }

            if (icon is not null)
            {
                action.SetIcon(icon);
            }

            if (context.ApiLevel >= HeaderRenderer.ActionEnabledApiLevel)
            {
                action.SetEnabled(button.IsEnabled);
            }

            strip.AddAction(action.Build()!);
            count++;
        }

        return count == 0 ? null : strip.Build();
    }

    // Map bar: Action.PAN (IsPanningEnabled) and map buttons with icons (ACTIONS_CONSTRAINTS_MAP without titles), at
    // most 4 together
    private static ActionStrip? MapActionStrip(CarMapTemplate model, CarAppRenderContext context, CarAppScreenState screen)
    {
        var strip = new ActionStrip.Builder();
        var count = 0;
        if (model.IsPanningEnabled)
        {
            strip.AddAction(NativeAction.Pan!);
            count++;
        }

        foreach (var button in model.MapButtons)
        {
            if (count >= MaxActions || button.IsHidden || context.Icon(screen, button.Image, CarAppImages.SmallPixels) is not { } icon)
            {
                continue;
            }

            var action = new NativeAction.Builder().SetIcon(icon)!.SetOnClickListener(context.Click(button, button.OnSelected))!;
            if (context.ApiLevel >= HeaderRenderer.ActionEnabledApiLevel)
            {
                action.SetEnabled(button.IsEnabled);
            }

            strip.AddAction(action.Build()!);
            count++;
        }

        return count == 0 ? null : strip.Build();
    }

    private sealed class PanModeListener(CarAppNavigationConnection connection) : Java.Lang.Object, IPanModeListener
    {
        public void OnPanModeChanged(bool p0) => connection.OnPanModeChanged(p0);
    }

    private sealed class RouteSelectedListener(CarAppNavigationConnection connection, CarTrip trip) : Java.Lang.Object, ItemList.IOnSelectedListener
    {
        public void OnSelected(int selectedIndex)
        {
            if (selectedIndex >= 0 && selectedIndex < trip.Routes.Count)
            {
                connection.OnRouteSelected(trip, trip.Routes[selectedIndex]);
            }
        }
    }

    private sealed class StartListener(CarAppNavigationConnection connection) : Java.Lang.Object, IOnClickListener
    {
        public void OnClick() => connection.OnStartPressed();
    }
}
