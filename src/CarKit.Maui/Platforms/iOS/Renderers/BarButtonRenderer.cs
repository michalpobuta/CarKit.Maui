using CarPlay;
using UIKit;

namespace CarKit.Maui.Renderers;

// CarBarButton → CPBarButton (Leading/TrailingNavigationBarButtons of the list, grid and pane); the title takes
// precedence over the image
internal static class BarButtonRenderer
{
    public static void Apply(ICPBarButtonProviding native, CarTemplate model, CarTemplate? previous, CarPlayTemplateState state, CarPlayRenderContext context)
    {
        var restyled = state.Restyled(context);
        if (previous is null || previous.LeadingBarButtons != model.LeadingBarButtons || restyled)
        {
            native.LeadingNavigationBarButtons = [.. model.LeadingBarButtons.Select(button => Button(button, state, context))];
        }

        if (previous is null || previous.TrailingBarButtons != model.TrailingBarButtons || restyled)
        {
            native.TrailingNavigationBarButtons = [.. model.TrailingBarButtons.Select(button => Button(button, state, context))];
        }
    }

    private static CPBarButton Button(CarBarButton model, CarPlayTemplateState state, CarPlayRenderContext context)
    {
        CPBarButton? native = null;
        if (model.Title is { } title)
        {
            native = new CPBarButton(title.Text, _ => context.Select(state, model));
        }
        else
        {
            var image = context.Image(state, "bar:" + model.Id, model.Image, CarPlayImages.BarButtonSize, loaded =>
            {
                if (native is not null)
                {
                    native.Image = loaded;
                }
            });
            native = new CPBarButton(image ?? new UIImage(), _ => context.Select(state, model));
        }

        native.Enabled = model.IsEnabled;
        return native;
    }
}
