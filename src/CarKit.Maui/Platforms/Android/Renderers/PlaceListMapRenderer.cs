using Android.Text;
using AndroidX.Car.App.Model;
using NativeCarLocation = AndroidX.Car.App.Model.CarLocation;
using NativeDistance = AndroidX.Car.App.Model.Distance;

namespace CarKit.Maui.Renderers;

// CarPointOfInterestTemplate → PlaceListMapTemplate (host map, POI categories) or ListTemplate with place rows
// (other categories; spec: "Category decisions (Stage 7)"). The choice is fixed per connection
// (CarAppRenderContext.UsePlaceListMap). Row: title, distance (DistanceSpan, required by PlaceListMapTemplate) +
// subtitle, summary, Metadata with Place; pin = marker icon (a row with a marker cannot have an image), in a list
// without a map the row image. Rows have no actions (ROW_CONSTRAINTS_SIMPLE), so a click = OnSelected or the main
// action of the CarPlay card.
internal static class PlaceListMapRenderer
{
    public static CarAppRendered Render(CarPointOfInterestTemplate model, CarAppRenderContext context, CarAppScreenState screen) =>
        context.UsePlaceListMap ? PlaceList(model, context, screen) : List(model, context, screen);

    private static CarAppRendered PlaceList(CarPointOfInterestTemplate model, CarAppRenderContext context, CarAppScreenState screen)
    {
        var title = model.Title?.Text ?? string.Empty;
        var builder = new PlaceListMapTemplate.Builder()
            .SetTitle(CarAppTemplateRenderers.Text(model.Title ?? string.Empty))!
            .SetHeaderAction(HeaderRenderer.Start(screen))!;
        if (HeaderRenderer.Strip(model, screen, context) is { } strip)
        {
            builder.SetActionStrip(strip);
        }

        // The host shows the refresh button in the header only when a listener is set (@RequiresCarApi(5))
        if (model.OnRefreshRequested is not null && context.ApiLevel >= CarPointsOfInterest.RefreshApiLevel)
        {
            builder.SetOnContentRefreshListener(new RefreshListener(context, model));
        }
        else if (model.OnRefreshRequested is not null)
        {
            context.ReportUnavailableRefresh(model, $"the host has Car App API level {context.ApiLevel}, the button needs {CarPointsOfInterest.RefreshApiLevel}");
        }

        if (model.IsLoading)
        {
            builder.SetLoading(true);
            CarAppHooks.Apply(model, builder, context);
            return new(builder.Build()!, new CarAppTemplateShape(CarAppTemplateTypes.PlaceListMap, title, IsLoading: true));
        }

        var (list, rows) = Items(model, context, screen, marker: true);
        builder.SetItemList(list.Build());
        CarAppHooks.Apply(model, builder, context);
        return new(builder.Build()!, new CarAppTemplateShape(CarAppTemplateTypes.PlaceListMap, title, IsLoading: false) { Sections = [new CarAppSectionShape(null, rows)] });
    }

    // Without a map (non-POI category): ListTemplate like the CarKit list, the place in the row metadata, the pin as the
    // row image; ListTemplate has no refresh button (final review I1: a warning instead of a silent skip)
    private static CarAppRendered List(CarPointOfInterestTemplate model, CarAppRenderContext context, CarAppScreenState screen)
    {
        var title = model.Title?.Text ?? string.Empty;
        if (model.OnRefreshRequested is not null)
        {
            context.ReportUnavailableRefresh(model, "apps without a point-of-interest category get a list without a map");
        }

        var builder = new ListTemplate.Builder();
        ListTemplateRenderer.ApplyHeader(builder, title, model, screen, context);
        if (model.IsLoading)
        {
            builder.SetLoading(true);
            CarAppHooks.Apply(model, builder, context);
            return new(builder.Build()!, new CarAppTemplateShape(CarAppTemplateTypes.List, title, IsLoading: true));
        }

        var (list, rows) = Items(model, context, screen, marker: false);
        builder.SetSingleList(list.Build());
        CarAppHooks.Apply(model, builder, context);
        return new(builder.Build()!, new CarAppTemplateShape(CarAppTemplateTypes.List, title, IsLoading: false) { Sections = [new CarAppSectionShape(null, rows)] });
    }

    // Stage 8: place rows also for a list in the Navigation module's MapWithContentTemplate (app map, no host markers)
    internal static (ItemList.Builder List, CarArray<CarAppRowShape> Rows) Items(
        CarPointOfInterestTemplate model, CarAppRenderContext context, CarAppScreenState screen, bool marker)
    {
        var list = new ItemList.Builder();
        var rows = new List<CarAppRowShape>();
        foreach (var point in model.Points)
        {
            list.AddItem(Row(point, context, screen, marker));
            rows.Add(new CarAppRowShape(RowRenderer.Title(point.Title)));
        }

        return (list, [.. rows]);
    }

    private static Row Row(CarPointOfInterest point, CarAppRenderContext context, CarAppScreenState screen, bool marker)
    {
        var row = new Row.Builder().SetTitle(CarAppTemplateRenderers.Text(point.Title.Text.Length > 0 ? point.Title : RowRenderer.Title(point.Title)))!;
        if (FirstLine(point) is { } first)
        {
            row.AddText(first);
        }

        if (point.Summary is { } summary)
        {
            row.AddText(CarAppTemplateRenderers.Text(summary));
        }

        // The pin is loaded in the background: the default marker for now (or a row without an image), a screen rebuild
        // after loading
        var pin = context.Icon(screen, point.PinImage, CarAppImages.SmallPixels);
        var place = new Place.Builder(NativeCarLocation.Create(point.Location.Latitude, point.Location.Longitude)!);
        if (marker)
        {
            var placeMarker = new PlaceMarker.Builder();
            if (pin is not null)
            {
                placeMarker.SetIcon(pin, PlaceMarker.TypeImage);
            }

            place.SetMarker(placeMarker.Build()!);
        }
        else if (pin is not null)
        {
            row.SetImage(pin);
        }

        row.SetMetadata(new Metadata.Builder().SetPlace(place.Build()!)!.Build()!);
        var (target, handler) = CarPointsOfInterest.AndroidAutoSelection(point);
        if (handler is not null)
        {
            row.SetOnClickListener(context.Click(target, handler));
        }

        return row.Build()!;
    }

    // The distance as a DistanceSpan on the first character, then " · " and the subtitle (a pattern from the Car App
    // Library documentation); without a distance (validator in Warn/Off) only the subtitle, and PlaceListMapTemplate will
    // reject the row (native error)
    private static Java.Lang.ICharSequence? FirstLine(CarPointOfInterest point)
    {
        if (point.Distance is not { } distance)
        {
            return point.Subtitle is { } subtitle ? new Java.Lang.String(subtitle.Text) : null;
        }

        var (value, unit) = CarDistanceUnits.AndroidAuto(distance);
        var text = new SpannableString(point.Subtitle is { } detail ? "  · " + detail.Text : " ");
        text.SetSpan(DistanceSpan.Create(NativeDistance.Create(value, unit)!), 0, 1, SpanTypes.InclusiveInclusive);
        return text;
    }

    // The model as of render time (like the rows' OnClickListener); every screen rebuild creates a new listener
    private sealed class RefreshListener(CarAppRenderContext context, CarPointOfInterestTemplate model) : Java.Lang.Object, IOnContentRefreshListener
    {
        public void OnContentRefreshRequested() => context.Run(model.OnRefreshRequested, context.Session.CreateContext(model), "RefreshRequested");
    }
}
