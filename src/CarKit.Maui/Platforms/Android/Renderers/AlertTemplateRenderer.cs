using AndroidX.Car.App.Model;

namespace CarKit.Maui.Renderers;

// CarAlertTemplate → Alert for AppManager.ShowAlert (Car App API 5). Without a callback: see CarAppLibraryHost.ShowAlert.
// Choosing an action closes the alert before the handler (spec: "Template decisions (Stage 3)").
internal static class AlertTemplateRenderer
{
    public static Alert Render(CarAlertTemplate model, CarAppRenderContext context, int alertId, TimeSpan duration, Func<Task> close)
    {
        var builder = new Alert.Builder(alertId, CarAppTemplateRenderers.Text(model.Title ?? " "), (long)duration.TotalMilliseconds);
        if (model.Subtitle is { } subtitle)
        {
            builder.SetSubtitle(CarAppTemplateRenderers.Text(subtitle));
        }

        // An alert is not a screen, so there is no rebuild after a URI image; the icon comes from the cache on the next show
        var modal = new CarAppScreenState { IsModal = true, CloseModal = close };
        if (context.Icon(modal, model.Icon, CarAppImages.SmallPixels) is { } icon)
        {
            builder.SetIcon(icon);
        }

        foreach (var action in model.Actions)
        {
            builder.AddAction(HeaderRenderer.Action(action, context, modal));
        }

        CarAppHooks.Apply(model, builder, context);
        return builder.Build()!;
    }
}
