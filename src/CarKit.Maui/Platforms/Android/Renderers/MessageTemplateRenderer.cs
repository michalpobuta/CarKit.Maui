using AndroidX.Car.App.Model;

namespace CarKit.Maui.Renderers;

// CarMessageTemplate → MessageTemplate (also the ActionSheet/Alert/VoiceControl fallback as a modal screen):
// content, icon (not together with loading), max. 2 actions; a modal screen's action closes it before the handler
internal static class MessageTemplateRenderer
{
    public static CarAppRendered Render(CarMessageTemplate model, CarAppRenderContext context, CarAppScreenState screen)
    {
        var title = model.Title?.Text;
        var builder = new MessageTemplate.Builder(CarAppTemplateRenderers.Text(model.Message.Text.Length > 0 ? model.Message : " "));
        if (!screen.InTab)
        {
            if (HeaderRenderer.UsesHeader(context))
            {
                builder.SetHeader(HeaderRenderer.Header(title, model, screen, context));
            }
            else
            {
#pragma warning disable CS0618 // Header requires Car App API 7; this is the path for older hosts
                if (!string.IsNullOrEmpty(title))
                {
                    builder.SetTitle(title);
                }

                builder.SetHeaderAction(HeaderRenderer.Start(screen));
                if (HeaderRenderer.Strip(model, screen, context) is { } strip)
                {
                    builder.SetActionStrip(strip);
                }
#pragma warning restore CS0618
            }
        }

        if (model.IsLoading)
        {
            builder.SetLoading(true);
        }
        else if (context.Icon(screen, model.Icon, CarAppImages.LargePixels) is { } icon)
        {
            builder.SetIcon(icon);
        }

        foreach (var action in model.Actions)
        {
            builder.AddAction(HeaderRenderer.Action(action, context, screen));
        }

        CarAppHooks.Apply(model, builder, context);
        return new(builder.Build()!, new CarAppTemplateShape(CarAppTemplateTypes.Message, screen.InTab ? null : title, model.IsLoading)
        {
            Message = model.Message.Text,
        });
    }
}
