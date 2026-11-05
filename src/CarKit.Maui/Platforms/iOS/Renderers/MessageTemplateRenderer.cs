using CarPlay;

namespace CarKit.Maui.Renderers;

// CarMessageTemplate → CPInformationTemplate (Stage 1 decision): content as the only item; Icon and IsLoading have no
// counterpart
internal static class MessageTemplateRenderer
{
    public static CarPlayTemplateState Render(CarMessageTemplate model, CarPlayRenderContext context)
    {
        var state = new CarPlayTemplateState(model);
        var native = new CPInformationTemplate(
            model.Title?.Text ?? string.Empty,
            CPInformationTemplateLayout.Leading,
            InformationTemplateRenderer.Items(Items(model)),
            InformationTemplateRenderer.Actions(model.Actions, state, context));
        state.Native = native;
        BarButtonRenderer.Apply(native, model, previous: null, state, context);
        return state;
    }

    public static void Update(CarPlayTemplateState state, CPInformationTemplate native, CarMessageTemplate model, CarPlayRenderContext context)
    {
        var previous = (CarMessageTemplate)state.Model;
        InformationTemplateRenderer.Apply(
            native, model.Title?.Text, previous.Title?.Text, Items(model), Items(previous), model.Actions, previous.Actions, state, context);
        BarButtonRenderer.Apply(native, model, previous, state, context);
    }

    private static CarArray<CarInformationItem> Items(CarMessageTemplate model) => [new CarInformationItem(null, model.Message)];
}
