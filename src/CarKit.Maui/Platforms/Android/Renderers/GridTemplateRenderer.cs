using AndroidX.Car.App.Model;

namespace CarKit.Maui.Renderers;

// CarGridTemplate → GridTemplate. An image still loading → an item in the loading state (a GridItem cannot have an
// image then), a failed one → the app icon (GridItem requires an image). GridItem has no disabled state: disabled =
// without OnClickListener.
internal static class GridTemplateRenderer
{
    public static CarAppRendered Render(CarGridTemplate model, CarAppRenderContext context, CarAppScreenState screen)
    {
        var title = model.Title?.Text ?? string.Empty;
        var builder = new GridTemplate.Builder();
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

        var shapeTitle = screen.InTab ? null : title;
        if (model.IsLoading)
        {
            builder.SetLoading(true);
            CarAppHooks.Apply(model, builder, context);
            return new(builder.Build()!, new CarAppTemplateShape(CarAppTemplateTypes.Grid, shapeTitle, IsLoading: true));
        }

        var list = new ItemList.Builder();
        var rows = new List<CarAppRowShape>();
        foreach (var gridItem in model.Items)
        {
            var item = new GridItem.Builder().SetTitle(CarAppTemplateRenderers.Text(gridItem.Title.Text.Length > 0 ? gridItem.Title : " "))!;
            if (gridItem.Text is { } text)
            {
                item.SetText(CarAppTemplateRenderers.Text(text));
            }

            var load = context.Load(screen, gridItem.Image, CarAppImages.LargePixels);
            if (load.Image is { } icon)
            {
                item.SetImage(icon);
            }
            else if (load.Pending is not null)
            {
                item.SetLoading(true);
            }
            else
            {
                item.SetImage(CarAppImages.AppIcon(context.Context));
            }

            // A GridItem in the loading state cannot have an OnClickListener (GridItem.Builder.build); it returns after
            // the image loads
            if (gridItem.OnSelected is not null && gridItem.IsEnabled && load.Pending is null)
            {
                item.SetOnClickListener(context.Click(gridItem, gridItem.OnSelected));
            }

            list.AddItem(item.Build()!);
            rows.Add(new CarAppRowShape(gridItem.Title.Text));
        }

        builder.SetSingleList(list.Build());
        CarAppHooks.Apply(model, builder, context);
        return new(builder.Build()!, new CarAppTemplateShape(CarAppTemplateTypes.Grid, shapeTitle, IsLoading: false) { Sections = [new CarAppSectionShape(null, [.. rows])] });
    }
}
