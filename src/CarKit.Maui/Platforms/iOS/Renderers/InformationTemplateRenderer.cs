using CarPlay;

namespace CarKit.Maui.Renderers;

// CarInformationTemplate → CPInformationTemplate (Title, Items, Actions with setters; Layout only in the constructor).
// CPTextButton has no disabled state, so an action with IsEnabled = false is skipped.
internal static class InformationTemplateRenderer
{
    public static CarPlayTemplateState Render(CarInformationTemplate model, CarPlayRenderContext context)
    {
        var state = new CarPlayTemplateState(model);
        var native = new CPInformationTemplate(model.Title?.Text ?? string.Empty, Layout(model.Layout), Items(model.Items), Actions(model.Actions, state, context));
        state.Native = native;
        BarButtonRenderer.Apply(native, model, previous: null, state, context);
        return state;
    }

    public static void Update(CarPlayTemplateState state, CPInformationTemplate native, CarInformationTemplate model, CarPlayRenderContext context)
    {
        var previous = (CarInformationTemplate)state.Model;
        Apply(native, model.Title?.Text, previous.Title?.Text, model.Items, previous.Items, model.Actions, previous.Actions, state, context);
        BarButtonRenderer.Apply(native, model, previous, state, context);
    }

    public static void Apply(
        CPInformationTemplate native,
        string? title,
        string? previousTitle,
        CarArray<CarInformationItem> items,
        CarArray<CarInformationItem> previousItems,
        CarArray<CarAction> actions,
        CarArray<CarAction> previousActions,
        CarPlayTemplateState state,
        CarPlayRenderContext context)
    {
        if (title != previousTitle)
        {
            native.Title = title ?? string.Empty;
        }

        if (items != previousItems)
        {
            native.Items = Items(items);
        }

        if (actions != previousActions)
        {
            native.Actions = Actions(actions, state, context);
        }
    }

    public static CPInformationItem[] Items(CarArray<CarInformationItem> items) =>
        [.. items.Select(item => new CPInformationItem(item.Title?.Text!, item.Detail.Text))];

    public static CPTextButton[] Actions(CarArray<CarAction> actions, CarPlayTemplateState state, CarPlayRenderContext context) =>
        [.. actions.Where(action => action.IsEnabled).Select(action => new CPTextButton(action.Title.Text, Style(action.Style), _ => context.Select(state, action)))];

    private static CPInformationTemplateLayout Layout(CarInformationLayout layout) =>
        layout == CarInformationLayout.TwoColumn ? CPInformationTemplateLayout.TwoColumn : CPInformationTemplateLayout.Leading;

    public static CPTextButtonStyle Style(CarActionStyle style) => style switch
    {
        CarActionStyle.Primary => CPTextButtonStyle.Confirm,
        CarActionStyle.Cancel => CPTextButtonStyle.Cancel,
        _ => CPTextButtonStyle.Normal,
    };
}
