using AndroidX.Car.App.Model;

namespace CarKit.Maui.Renderers;

// CarListTemplate → ListTemplate (Car App API < 8 or the SectionedItemTemplate=false switch). addSectionedList requires
// a non-empty header for every section; otherwise sections are flattened into one list.
internal static class ListTemplateRenderer
{
    public static CarAppRendered Render(CarListTemplate model, CarAppRenderContext context, CarAppScreenState screen)
    {
        var title = model.Title?.Text ?? string.Empty;
        var builder = new ListTemplate.Builder();
        if (!screen.InTab)
        {
            ApplyHeader(builder, title, model, screen, context);
        }

        var shapeTitle = screen.InTab ? null : title;
        if (model.IsLoading)
        {
            builder.SetLoading(true);
            CarAppHooks.Apply(model, builder, context);
            return new(builder.Build()!, new CarAppTemplateShape(CarAppTemplateTypes.List, shapeTitle, IsLoading: true));
        }

        var sections = model.Sections.Where(s => !s.Items.IsEmpty).ToList();
        var shapes = new List<CarAppSectionShape>();
        if (sections.Count > 1 && sections.All(s => !string.IsNullOrEmpty(s.Header?.Text)))
        {
            foreach (var section in sections)
            {
                var (list, rows) = Items(section.Items, context, screen);
                builder.AddSectionedList(SectionedItemList.Create(list.Build(), section.Header!.Text));
                shapes.Add(new CarAppSectionShape(section.Header.Text, rows));
            }
        }
        else
        {
            var (list, rows) = Items(sections.SelectMany(s => s.Items), context, screen);
            if (model.EmptyViewTitle is { } empty && rows.IsEmpty)
            {
                list.SetNoItemsMessage(empty.Text);
            }

            builder.SetSingleList(list.Build());
            shapes.Add(new CarAppSectionShape(null, rows));
        }

        CarAppHooks.Apply(model, builder, context);
        return new(builder.Build()!, new CarAppTemplateShape(CarAppTemplateTypes.List, shapeTitle, IsLoading: false) { Sections = [.. shapes] });
    }

    public static void ApplyHeader(ListTemplate.Builder builder, string title, CarTemplate model, CarAppScreenState screen, CarAppRenderContext context)
    {
        if (HeaderRenderer.UsesHeader(context))
        {
            builder.SetHeader(HeaderRenderer.Header(title, model, screen, context));
            return;
        }

#pragma warning disable CS0618 // Header requires Car App API 7; this is the path for older hosts
        builder.SetTitle(title)!.SetHeaderAction(HeaderRenderer.Start(screen));
        if (HeaderRenderer.Strip(model, screen, context) is { } strip)
        {
            builder.SetActionStrip(strip);
        }
#pragma warning restore CS0618
    }

    public static (ItemList.Builder List, CarArray<CarAppRowShape> Rows) Items(
        IEnumerable<CarListTemplateItem> items, CarAppRenderContext context, CarAppScreenState screen, bool allowToggle = true)
    {
        var list = new ItemList.Builder();
        var rows = new List<CarAppRowShape>();
        foreach (var item in items)
        {
            // Conversation: ConversationItem from Car App API 7 (RowListConstraints accepts Row and ConversationItem),
            // Row below;
            // without messages (validator in Warn/Off) also Row, because ConversationItem.build() would throw and break
            // the whole list
            if (item is CarConversationItem { Messages.IsEmpty: false } conversation && ConversationRenderer.Supported(context))
            {
                var (native, conversationShape) = ConversationRenderer.Render(conversation, context, screen);
                list.AddItem(native);
                rows.Add(conversationShape);
                continue;
            }

            var (row, shape) = RowRenderer.Render(item, context, screen, allowToggle);
            list.AddItem(row);
            rows.Add(shape);
        }

        return (list, [.. rows]);
    }
}
