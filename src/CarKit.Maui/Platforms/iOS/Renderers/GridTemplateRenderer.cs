using CarPlay;
using UIKit;

namespace CarKit.Maui.Renderers;

// CarGridTemplate → CPGridTemplate; update through UpdateTitle/UpdateGridButtons (iOS 15), a URI image through
// CPGridButton.UpdateImage (iOS 26) or UpdateGridButtons again (the image is already in the cache)
internal static class GridTemplateRenderer
{
    public static CarPlayTemplateState Render(CarGridTemplate model, CarPlayRenderContext context)
    {
        var state = new CarPlayTemplateState(model);
        CPGridTemplate? native = null;
        var buttons = Buttons(model, state, context, () => native);
        native = new CPGridTemplate(model.Title?.Text ?? string.Empty, buttons);
        state.Native = native;
        BarButtonRenderer.Apply(native, model, previous: null, state, context);
        return state;
    }

    public static void Update(CarPlayTemplateState state, CPGridTemplate native, CarGridTemplate model, CarPlayRenderContext context)
    {
        var previous = (CarGridTemplate)state.Model;
        if (previous.Title?.Text != model.Title?.Text)
        {
            native.UpdateTitle(model.Title?.Text ?? string.Empty);
        }

        if (previous.Items != model.Items || state.Restyled(context))
        {
            native.UpdateGridButtons(Buttons(model, state, context, () => native));
        }

        BarButtonRenderer.Apply(native, model, previous, state, context);
    }

    private static CPGridButton[] Buttons(CarGridTemplate model, CarPlayTemplateState state, CarPlayRenderContext context, Func<CPGridTemplate?> native) =>
        [.. model.Items.Select(item =>
        {
            CPGridButton? button = null;
            var image = context.Image(state, "grid:" + item.Id, item.Image, CarPlayImages.GridSize, loaded =>
            {
                if (OperatingSystem.IsIOSVersionAtLeast(26))
                {
                    button?.UpdateImage(loaded);
                }
                else if (native() is { } grid)
                {
                    grid.UpdateGridButtons(Buttons((CarGridTemplate)state.Model, state, context, native));
                }
            });
            button = NewButton([.. item.Title.Variants], image ?? context.Placeholder(CarPlayImages.GridSize), _ => context.Select(state, item));
            button.Enabled = item.IsEnabled;
            return button;
        })];

    // iOS 26 replaces the constructor with a variant with a message configuration (null = a plain button)
    private static CPGridButton NewButton(string[] titles, UIImage image, Action<CPGridButton> handler) =>
        OperatingSystem.IsIOSVersionAtLeast(26) ? new CPGridButton(titles, image, null!, handler) : LegacyButton(titles, image, handler);

#pragma warning disable CA1422 // The constructor without a message configuration is deprecated since iOS 26; this is the path for iOS 15–25
    private static CPGridButton LegacyButton(string[] titles, UIImage image, Action<CPGridButton> handler) => new(titles, image, handler);
#pragma warning restore CA1422
}
