using AndroidX.Car.App.Model;

namespace CarKit.Maui.Renderers;

// CarInformationTemplate → PaneTemplate: a label–value pair = a row (title = label, text = value), pane actions
// (max. 2, one Primary). A Pane without rows and without loading is rejected by Car App Library → a row with a title.
// The Layout has no counterpart.
internal static class PaneTemplateRenderer
{
    public static CarAppRendered Render(CarInformationTemplate model, CarAppRenderContext context, CarAppScreenState screen)
    {
        var title = model.Title?.Text ?? string.Empty;
        var pane = new Pane.Builder();
        var rows = new List<CarAppRowShape>();
        if (model.IsLoading)
        {
            pane.SetLoading(true);
        }
        else
        {
            foreach (var item in model.Items)
            {
                var label = item.Title ?? item.Detail;
                var row = new Row.Builder().SetTitle(CarAppTemplateRenderers.Text(label.Text.Length > 0 ? label : " "))!;
                if (item.Title is not null)
                {
                    row.AddText(CarAppTemplateRenderers.Text(item.Detail));
                }

                pane.AddRow(row.Build()!);
                rows.Add(new CarAppRowShape(RowRenderer.Title(label)));
            }

            if (rows.Count == 0)
            {
                pane.AddRow(new Row.Builder().SetTitle(RowRenderer.Title(title))!.Build()!);
                rows.Add(new CarAppRowShape(RowRenderer.Title(title)));
            }
        }

        foreach (var action in model.Actions)
        {
            pane.AddAction(HeaderRenderer.Action(action, context, screen));
        }

        var builder = new PaneTemplate.Builder(pane.Build()!);
        if (!screen.InTab)
        {
            if (HeaderRenderer.UsesHeader(context))
            {
                builder.SetHeader(HeaderRenderer.Header(title, model, screen, context));
            }
            else
            {
#pragma warning disable CS0618 // Header requires Car App API 7; this is the path for older hosts
                builder.SetTitle(title)!.SetHeaderAction(HeaderRenderer.Start(screen));
                if (HeaderRenderer.Strip(model, screen, context) is { } strip)
                {
                    builder.SetActionStrip(strip);
                }
#pragma warning restore CS0618
            }
        }

        CarAppHooks.Apply(model, builder, context);
        return new(builder.Build()!, new CarAppTemplateShape(CarAppTemplateTypes.Pane, screen.InTab ? null : title, model.IsLoading)
        {
            Sections = [new CarAppSectionShape(null, [.. rows])],
        });
    }
}
