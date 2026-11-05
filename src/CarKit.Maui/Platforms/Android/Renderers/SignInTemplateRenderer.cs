using AndroidX.Car.App.Model;
using AndroidX.Car.App.Model.SignIn;
using CarKitText = CarKit.Maui.CarText;

namespace CarKit.Maui.Renderers;

// AndroidAutoSignInTemplate → SignInTemplate (Car App API 2; spec: "Platform template decisions (Stage 4)"). Parked
// only: the host hides the content, content actions and the provider action require ParkedOnlyOnClickListener.
// SignInTemplate has no Header: the title and header action (AppIcon/Back) go through the template setters, the bar
// through ActionStrip (max. one action with a title). Shape: the method kind in Message (a refresh rule); the template
// does not count toward the step limit anyway.
internal static class SignInTemplateRenderer
{
    public static CarAppRendered Render(AndroidAutoSignInTemplate model, CarAppRenderContext context, CarAppScreenState screen)
    {
        var title = Text(model.Title) ?? " ";
        var builder = new SignInTemplate.Builder(Method(model, context, screen))
            .SetTitle(title)!
            .SetHeaderAction(HeaderRenderer.Start(screen))!;
        if (Text(model.Instructions) is { } instructions)
        {
            builder.SetInstructions(instructions);
        }

        if (Text(model.AdditionalText) is { } additional)
        {
            builder.SetAdditionalText(additional);
        }

        if (model.IsLoading)
        {
            builder.SetLoading(true);
        }

        if (HeaderRenderer.Strip(model, screen, context) is { } strip)
        {
            builder.SetActionStrip(strip);
        }

        foreach (var action in model.Actions)
        {
            builder.AddAction(HeaderRenderer.Action(action, context, screen, parkedOnly: true));
        }

        CarAppHooks.Apply(model, builder, context);
        return new(builder.Build()!, new CarAppTemplateShape(CarAppTemplateTypes.SignIn, title, model.IsLoading) { Message = MethodName(model.Method) });
    }

    public static string MethodName(AndroidAutoSignInMethod method) => method switch
    {
        AndroidAutoPinSignIn => "Pin",
        AndroidAutoQrCodeSignIn => "QrCode",
        AndroidAutoInputSignIn => "Input",
        AndroidAutoProviderSignIn => "Provider",
        _ => method.GetType().Name,
    };

    private static string? Text(CarKitText? text) => string.IsNullOrEmpty(text?.Text) ? null : text.Text;

    // QR from Car App API 4 is decided by the validator (the QrCode variant in androidauto.json), so no API level check here
    private static SignInTemplate.ISignInMethod Method(AndroidAutoSignInTemplate model, CarAppRenderContext context, CarAppScreenState screen) => model.Method switch
    {
        AndroidAutoPinSignIn pin => new PinSignInMethod(pin.Pin),
        AndroidAutoQrCodeSignIn qr => new QRCodeSignInMethod(Android.Net.Uri.Parse(qr.Uri.AbsoluteUri)!),
        AndroidAutoInputSignIn input => Input(model, input, context),
        AndroidAutoProviderSignIn provider => new ProviderSignInMethod(
            HeaderRenderer.Action(provider.Action, context, screen, parkedOnly: true, icon: context.Icon(screen, provider.Action.Image, CarAppImages.SmallPixels))),
        _ => throw new NotSupportedException($"CarKit does not render {model.Method.GetType().Name} on Android Auto."),
    };

    private static InputSignInMethod Input(AndroidAutoSignInTemplate model, AndroidAutoInputSignIn input, CarAppRenderContext context)
    {
        var builder = new InputSignInMethod.Builder(new InputCallback(model, input, context))
            .SetInputType(input.InputType == AndroidAutoInputType.Password ? InputSignInMethod.InputTypePassword : InputSignInMethod.InputTypeDefault)!
            .SetKeyboardType(input.KeyboardType switch
            {
                AndroidAutoKeyboardType.Email => InputSignInMethod.KeyboardEmail,
                AndroidAutoKeyboardType.Phone => InputSignInMethod.KeyboardPhone,
                AndroidAutoKeyboardType.Number => InputSignInMethod.KeyboardNumber,
                _ => InputSignInMethod.KeyboardDefault,
            })!;
        if (Text(input.Hint) is { } hint)
        {
            builder.SetHint(hint);
        }

        if (input.DefaultValue is { Length: > 0 } value)
        {
            builder.SetDefaultValue(value);
        }

        if (Text(input.ErrorMessage) is { } error)
        {
            builder.SetErrorMessage(error);
        }

        return builder.Build()!;
    }

    // Every rebuild creates a new callback with the current model (the app's reply through UpdateAsync goes to the new
    // handler)
    private sealed class InputCallback(AndroidAutoSignInTemplate model, AndroidAutoInputSignIn input, CarAppRenderContext context)
        : Java.Lang.Object, IInputCallback
    {
        public void OnInputSubmitted(string? text) =>
            context.Run(input.OnInputSubmitted, new CarTextInputContext(context.Session, model, text ?? string.Empty, context.Session.ConnectionToken), "InputSubmitted");

        public void OnInputTextChanged(string? text)
        {
        }
    }
}
