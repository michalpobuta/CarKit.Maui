using Android.Runtime;
using AndroidX.Car.App.Model;

namespace CarKit.Maui.Renderers;

// CarListTemplate → SectionedItemTemplate (Car App API 8, spec: "Template decisions (Stage 3)"): section rows →
// RowSection (title = header), CarListImageRowItem → GridSection (title = row text, image selection → OnImageSelected)
internal static class SectionedListRenderer
{
    public static CarAppRendered Render(CarListTemplate model, CarAppRenderContext context, CarAppScreenState screen)
    {
        var title = model.Title?.Text ?? string.Empty;
        var builder = new SectionedItemTemplate.Builder();
        if (!screen.InTab)
        {
            builder.SetHeader(HeaderRenderer.Header(title, model, screen, context));
        }

        var shapeTitle = screen.InTab ? null : title;
        if (model.IsLoading)
        {
            builder.SetLoading(true);
            CarAppHooks.Apply(model, builder, context);
            return new(builder.Build()!, new CarAppTemplateShape(CarAppTemplateTypes.Sectioned, shapeTitle, IsLoading: true));
        }

        var shapes = new List<CarAppSectionShape>();
        foreach (var section in model.Sections)
        {
            var header = section.Header?.Text;
            var rows = new List<CarListTemplateItem>();
            foreach (var item in section.Items)
            {
                if (item is CarListImageRowItem images)
                {
                    AddRows(builder, ref header, rows, context, screen, shapes);
                    builder.AddSection(Grid(images, context, screen, shapes));
                }
                else
                {
                    rows.Add(item);
                }
            }

            AddRows(builder, ref header, rows, context, screen, shapes);
        }

        if (shapes.Count == 0)
        {
            var empty = new RowSection.Builder();
            if (model.EmptyViewTitle is { } message)
            {
                empty.SetNoItemsMessage(CarAppTemplateRenderers.Text(message));
            }

            builder.AddSection(empty.Build()!);
            shapes.Add(new CarAppSectionShape(null, []));
        }

        if (model.Sections.Any(s => s.IndexTitle is not null))
        {
            builder.SetAlphabeticalIndexingAllowed(true);
        }

        CarAppHooks.Apply(model, builder, context);
        return new(builder.Build()!, new CarAppTemplateShape(CarAppTemplateTypes.Sectioned, shapeTitle, IsLoading: false) { Sections = [.. shapes] });
    }

    // Subsequent plain rows → one RowSection; the model's section header goes to the first part of the section
    private static void AddRows(
        SectionedItemTemplate.Builder builder, ref string? header, List<CarListTemplateItem> rows, CarAppRenderContext context, CarAppScreenState screen, List<CarAppSectionShape> shapes)
    {
        if (rows.Count == 0)
        {
            return;
        }

        var section = new RowSection.Builder();
        if (!string.IsNullOrEmpty(header))
        {
            section.SetTitle(CarAppTemplateRenderers.Text(header));
        }

        var natives = new JavaList<Row>();
        var rowShapes = new List<CarAppRowShape>();
        foreach (var item in rows)
        {
            var (row, shape) = RowRenderer.Render(item, context, screen);
            natives.Add(row);
            rowShapes.Add(shape);
        }

        section.SetItems(natives);
        builder.AddSection(section.Build()!);
        shapes.Add(new CarAppSectionShape(string.IsNullOrEmpty(header) ? null : header, [.. rowShapes]));
        rows.Clear();
        header = null;
    }

    private static GridSection Grid(CarListImageRowItem model, CarAppRenderContext context, CarAppScreenState screen, List<CarAppSectionShape> shapes)
    {
        var section = new GridSection.Builder();
        section.SetTitle(CarAppTemplateRenderers.Text(RowRenderer.Title(model.Text)));
        var items = new JavaList<GridItem>();
        var itemShapes = new List<CarAppRowShape>();
        for (var i = 0; i < model.Images.Count; i++)
        {
            var index = i;
            var item = new GridItem.Builder();
            var load = context.Load(screen, model.Images[i], CarAppImages.LargePixels);
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
            if (model.IsEnabled && load.Pending is null)
            {
                item.SetOnClickListener(new ImageClick(context, model, index));
            }

            items.Add(item.Build()!);
            itemShapes.Add(new CarAppRowShape(index.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        section.SetItems(items);
        shapes.Add(new CarAppSectionShape(RowRenderer.Title(model.Text), [.. itemShapes]));
        return section.Build()!;
    }

    private sealed class ImageClick(CarAppRenderContext context, CarListImageRowItem row, int index) : Java.Lang.Object, IOnClickListener
    {
        public void OnClick() =>
            context.Run(row.OnImageSelected, new CarImageRowContext(context.Session, row, index, context.Session.ConnectionToken), "SelectImage");
    }
}
