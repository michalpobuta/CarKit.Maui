using CarPlay;

namespace CarKit.Maui.Renderers;

// CarAlertTemplate → CPAlertTemplate (immutable: a text change = replacement, handlers from the current model).
// Choosing an action closes the alert before the handler (spec: "Template decisions (Stage 3)"); Subtitle, Icon and
// Duration are AA only.
internal static class AlertTemplateRenderer
{
    public static CarPlayTemplateState Render(CarAlertTemplate model, CarPlayRenderContext context)
    {
        var state = new CarPlayTemplateState(model);
        state.Native = new CPAlertTemplate([.. model.Title?.Variants ?? []], WithinLimit(Actions(model.Actions, state, context)));
        return state;
    }

    public static CPAlertAction[] Actions(CarArray<CarAction> actions, CarPlayTemplateState state, CarPlayRenderContext context) =>
        [.. actions.Where(action => action.IsEnabled).Select(action => new CPAlertAction(action.Title.Text, Style(action.Style), _ => context.SelectModalAction(state, action)))];

    // Stage 10: on overflow CarPlay keeps the LAST actions (the CPAlertTemplate header), and the primary action is first
    // in the model; the validator truncates with Truncate, this step protects the primary action in the Warn and Off
    // modes and with OnOverflow = Throw without Strict
    private static CPAlertAction[] WithinLimit(CPAlertAction[] actions)
    {
        var limit = (int)CPAlertTemplate.MaximumActionCount;
        return limit > 0 && actions.Length > limit ? actions[..limit] : actions;
    }

    private static CPAlertActionStyle Style(CarActionStyle style) => style switch
    {
        CarActionStyle.Cancel => CPAlertActionStyle.Cancel,
        CarActionStyle.Destructive => CPAlertActionStyle.Destructive,
        _ => CPAlertActionStyle.Default,
    };
}
