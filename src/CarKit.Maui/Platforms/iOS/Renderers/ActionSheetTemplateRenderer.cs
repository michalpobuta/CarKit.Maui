using CarPlay;

namespace CarKit.Maui.Renderers;

// CarActionSheetTemplate → CPActionSheetTemplate (immutable); choosing an action closes the sheet before the handler
internal static class ActionSheetTemplateRenderer
{
    public static CarPlayTemplateState Render(CarActionSheetTemplate model, CarPlayRenderContext context)
    {
        var state = new CarPlayTemplateState(model);
        state.Native = new CPActionSheetTemplate(model.Title?.Text!, model.Message?.Text!, AlertTemplateRenderer.Actions(model.Actions, state, context));
        return state;
    }
}
