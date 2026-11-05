using CarPlay;
using CoreGraphics;
using UIKit;

namespace CarKit.Maui.Renderers;

// CarPlayContactTemplate → CPContactTemplate (spec: "Platform template decisions (Stage 4)"). CPContact has setters,
// but CPButton does not (image and handler only in the constructor), so a change of data, buttons or theme = a new
// CPContact assigned to Contact. Message is sent by Siri (CPContactMessageButton without a handler); handlers read the
// current model. CPContact only via initWithName:image: (image required, transparent until loaded): after a plain init
// CarPlay ignores the setters (device test: Name empty, Actions null).
internal static class ContactTemplateRenderer
{
    // CPButton.MaximumImageSize is in the binding, but without a minimum version in the documentation; the contact
    // button size in points
    private static readonly CGSize _buttonSize = new(40, 40);
    private static readonly CGSize _contactSize = new(64, 64);

    public static CarPlayTemplateState Render(CarPlayContactTemplate model, CarPlayRenderContext context)
    {
        var state = new CarPlayTemplateState(model);
        CPContactTemplate? native = null;
        native = new CPContactTemplate(Contact(model, state, context, () => native));
        state.Native = native;
        BarButtonRenderer.Apply(native, model, previous: null, state, context);
        return state;
    }

    public static void Update(CarPlayTemplateState state, CPContactTemplate native, CarPlayContactTemplate model, CarPlayRenderContext context)
    {
        var previous = (CarPlayContactTemplate)state.Model;
        if (!SameContact(previous, model) || state.Restyled(context))
        {
            native.Contact = Contact(model, state, context, () => native);
        }

        BarButtonRenderer.Apply(native, model, previous, state, context);
    }

    private static bool SameContact(CarPlayContactTemplate previous, CarPlayContactTemplate model) =>
        previous.Name == model.Name && previous.Subtitle == model.Subtitle && previous.InformativeText == model.InformativeText
        && Equals(previous.Image, model.Image) && previous.Buttons == model.Buttons;

    // A URI/stream image: after loading, a new CPContact with the image from the cache (CPButton.Image is read-only)
    private static CPContact Contact(CarPlayContactTemplate model, CarPlayTemplateState state, CarPlayRenderContext context, Func<CPContactTemplate?> native)
    {
        void Rebuild(UIImage _)
        {
            if (native() is { } template)
            {
                template.Contact = Contact((CarPlayContactTemplate)state.Model, state, context, native);
            }
        }

        var image = context.Image(state, "contact:" + model.Id, model.Image, _contactSize, Rebuild);
        var contact = new CPContact(model.Name.Text, image ?? context.Placeholder(_contactSize))
        {
            Actions = [.. model.Buttons.Select(button => Button(button, state, context, Rebuild)).OfType<CPButton>()],
        };
        if (model.Subtitle is { } subtitle)
        {
            contact.Subtitle = subtitle.Text;
        }

        if (model.InformativeText is { } informative)
        {
            contact.InformativeText = informative.Text;
        }

        return contact;
    }

    // Message without PhoneOrEmail and Custom without Title and Image are reported by the validator; here (Warn/Off)
    // they are skipped
    private static CPButton? Button(CarPlayContactButton model, CarPlayTemplateState state, CarPlayRenderContext context, Action<UIImage> rebuild)
    {
        var image = context.Image(state, "contact-button:" + model.Id, model.Image, _buttonSize, rebuild);
        Action<CPButton> handler = _ => context.Select(state, model);
        CPButton? button = model.Kind switch
        {
            CarPlayContactButtonKind.Call => image is null ? new CPContactCallButton(handler) : new CPContactCallButton(image, handler),
            CarPlayContactButtonKind.Directions => image is null ? new CPContactDirectionsButton(handler) : new CPContactDirectionsButton(image, handler),
            CarPlayContactButtonKind.Message when !string.IsNullOrWhiteSpace(model.PhoneOrEmail) => new CPContactMessageButton(model.PhoneOrEmail),
            CarPlayContactButtonKind.Custom when model.Title is not null || model.Image is not null =>
                new CPButton(image ?? context.Placeholder(_buttonSize), handler),
            _ => null,
        };
        if (button is not null && model.Title is { } title)
        {
            button.Title = title.Text;
        }

        return button;
    }
}
