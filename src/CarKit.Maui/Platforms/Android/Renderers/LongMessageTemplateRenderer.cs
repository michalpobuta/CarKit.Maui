using AndroidX.Car.App.Model;

namespace CarKit.Maui.Renderers;

// CarLongMessageTemplate → LongMessageTemplate (Car App API 2, parked only): title, content, max. 2 actions, ActionStrip
internal static class LongMessageTemplateRenderer
{
    public static CarAppRendered Render(CarLongMessageTemplate model, CarAppRenderContext context, CarAppScreenState screen)
    {
        var title = model.Title?.Text ?? " ";
        var builder = new LongMessageTemplate.Builder(model.Message.Length > 0 ? model.Message : " ")
            .SetTitle(title)!
            .SetHeaderAction(HeaderRenderer.Start(screen))!;
        if (HeaderRenderer.Strip(model, screen, context) is { } strip)
        {
            builder.SetActionStrip(strip);
        }

        foreach (var action in model.Actions)
        {
            builder.AddAction(HeaderRenderer.Action(action, context, screen, parkedOnly: true));
        }

        CarAppHooks.Apply(model, builder, context);
        return new(builder.Build()!, new CarAppTemplateShape(CarAppTemplateTypes.LongMessage, title, IsLoading: false) { Message = model.Message });
    }
}
