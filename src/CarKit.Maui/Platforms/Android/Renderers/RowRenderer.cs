using AndroidX.Car.App.Model;

namespace CarKit.Maui.Renderers;

// CarListItem → Row (title, detail, image, Toggle, browsable). A row with a Toggle cannot have an OnClickListener
// (Row.Builder), so OnSelected then works only on CarPlay. CarListImageRowItem outside SectionedItemTemplate → a Row
// with the first image. CarConversationItem here only as a fallback (Car App API < 7 or SectionedItemTemplate): title,
// last message, image.
internal static class RowRenderer
{
    // Row.Builder.setEnabled and Toggle.Builder.setEnabled have @RequiresCarApi(5)
    private const int EnabledApiLevel = 5;

    public static (Row Row, CarAppRowShape Shape) Render(CarListTemplateItem item, CarAppRenderContext context, CarAppScreenState screen, bool allowToggle = true)
    {
        var row = new Row.Builder();
        string title;
        bool? toggleState = null;
        switch (item)
        {
            case CarListItem model:
                title = Title(model.Text);
                row.SetTitle(CarAppTemplateRenderers.Text(model.Text.Text.Length > 0 ? model.Text : title));
                if (model.Detail is { } detail)
                {
                    row.AddText(CarAppTemplateRenderers.Text(detail));
                }

                if (context.Icon(screen, model.Image, CarAppImages.SmallPixels) is { } icon)
                {
                    row.SetImage(icon);
                }

                if (allowToggle && model.Toggle is { } toggle)
                {
                    var native = new Toggle.Builder(new ToggleListener(context, model)).SetChecked(toggle.IsChecked)!;
                    if (context.ApiLevel >= EnabledApiLevel)
                    {
                        native.SetEnabled(model.IsEnabled);
                    }

                    row.SetToggle(native.Build()!);
                    toggleState = toggle.IsChecked;
                }
                else if (model.OnSelected is not null && model.IsEnabled)
                {
                    // On hosts without Row.setEnabled (API < 5) a disabled row simply gets no OnClickListener;
                    // a "browsable" row must have an OnClickListener
                    row.SetOnClickListener(context.Click(model, model.OnSelected));
                    row.SetBrowsable(model.IsBrowsable);
                }

                break;
            case CarListImageRowItem images:
                title = Title(images.Text);
                row.SetTitle(CarAppTemplateRenderers.Text(images.Text.Text.Length > 0 ? images.Text : title));
                if (!images.Images.IsEmpty && context.Icon(screen, images.Images[0], CarAppImages.SmallPixels) is { } first)
                {
                    row.SetImage(first);
                }

                if (images.OnSelected is not null && images.IsEnabled)
                {
                    row.SetOnClickListener(context.Click(images, images.OnSelected));
                }

                break;
            case CarConversationItem conversation:
                title = Title(conversation.Title);
                row.SetTitle(CarAppTemplateRenderers.Text(conversation.Title.Text.Length > 0 ? conversation.Title : title));
                if (!conversation.Messages.IsEmpty)
                {
                    var last = conversation.Messages[conversation.Messages.Count - 1];
                    row.AddText((last.Sender ?? conversation.Self).Name + ": " + last.Text);
                }

                if (context.Icon(screen, conversation.Image, CarAppImages.SmallPixels) is { } avatar)
                {
                    row.SetImage(avatar);
                }

                break;
            default:
                throw new NotSupportedException($"CarKit does not render {item.GetType().Name} on Android Auto.");
        }

        if (context.ApiLevel >= EnabledApiLevel)
        {
            row.SetEnabled(item.IsEnabled);
        }

        return (row.Build()!, new CarAppRowShape(title, toggleState));
    }

    // A row title cannot be empty (Row.Builder.build)
    public static string Title(CarText text) => text.Text.Length > 0 ? text.Text : " ";

    private sealed class ToggleListener(CarAppRenderContext context, CarListItem item) : Java.Lang.Object, Toggle.IOnCheckedChangeListener
    {
        public void OnCheckedChange(bool isChecked) =>
            context.Run(item.Toggle?.OnToggled, new CarToggleContext(context.Session, item, isChecked, context.Session.ConnectionToken), "Toggle");
    }
}
