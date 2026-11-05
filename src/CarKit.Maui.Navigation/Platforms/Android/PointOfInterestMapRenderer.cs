using AndroidX.Car.App.Model;
using AndroidX.Car.App.Navigation.Model;
using CarKit.Maui.Renderers;

namespace CarKit.Maui.Navigation;

// Stage 8: CarPointOfInterestTemplate with the app map (spec: "Navigation module decisions (Stage 8)", POI and weather
// with the app map): MapWithContentTemplate (API 7) with a MapController and a ListTemplate of place rows like the
// Stage 7 list (distance, subtitle, Place); the app draws the map and pins on CarScreen. The choice is fixed per
// connection (CarAppNavigationModule).
internal static class PointOfInterestMapRenderer
{
    public static CarAppRendered Render(CarPointOfInterestTemplate model, CarAppRenderContext context, CarAppScreenState screen)
    {
        var title = model.Title?.Text ?? string.Empty;
        var content = new ListTemplate.Builder();
        ListTemplateRenderer.ApplyHeader(content, title, model, screen, context);
        CarAppTemplateShape shape;
        if (model.IsLoading)
        {
            content.SetLoading(true);
            shape = new CarAppTemplateShape(CarAppTemplateTypes.MapWithContent, title, IsLoading: true);
        }
        else
        {
            var (list, rows) = PlaceListMapRenderer.Items(model, context, screen, marker: false);
            content.SetSingleList(list.Build());
            shape = new CarAppTemplateShape(CarAppTemplateTypes.MapWithContent, title, IsLoading: false) { Sections = [new CarAppSectionShape(null, rows)] };
        }

        if (model.OnRefreshRequested is not null)
        {
            context.ReportUnavailableRefresh(model, "the place list on the app's map (MapWithContentTemplate) has no refresh button");
        }

        var builder = new MapWithContentTemplate.Builder()
            .SetContentTemplate(content.Build()!)!
            .SetMapController(new MapController.Builder().Build()!)!;
        CarAppHooks.Apply(model, builder, context);
        return new(builder.Build()!, shape);
    }
}
