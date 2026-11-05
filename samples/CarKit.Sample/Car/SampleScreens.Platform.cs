using CarKit.Maui;

namespace CarKit.Sample.Car;

// Stage 4: single-platform templates (spec: "Platform template decisions (Stage 4)"). The sample runs in the Parking
// category with the default UnsupportedTemplateBehavior.UseFallbackOrThrow: on a platform without the template it shows
// an explicit fallback (contact, sign-in), a built-in one (voice on Android Auto), or an alert with the
// validator/exception message. Contact and voice on CarPlay in full form: build with
// -p:CarKitCategories=Parking%3BCommunication%3BNavigation (docs/manual-verification.md).
public sealed partial class SampleScreens
{
    private const string PlatformId = "platform";
    private const string ContactId = "contact";
    private const string SignInMenuId = "sign-in";
    private const string PinSignInId = "sign-in-pin";
    private const string QrSignInId = "sign-in-qr";
    private const string PasswordSignInId = "sign-in-password";
    private const string ProviderSignInId = "sign-in-google";
    private const string VoiceId = "voice";
    private const string Phone = "+1 555 010 2030";

    private static readonly TimeSpan _voiceStep = TimeSpan.FromSeconds(2);

    private CarTemplate? RebuildPlatform(string id) => id switch
    {
        PlatformId => Platform(),
        ContactId => Contact(),
        SignInMenuId => SignInMenu(),
        PinSignInId => PinSignIn(),
        QrSignInId => QrSignIn(),
        PasswordSignInId => PasswordSignIn(error: null),
        ProviderSignInId => ProviderSignIn(),
        _ => null,
    };

    private CarListTemplate Platform() => new("Platform")
    {
        Id = PlatformId,
        Sections =
        [
            new CarSection([
                Entry("Contact", "CarPlayContactTemplate (CarPlay)", Symbol("person.crop.circle", "info"), PushContactAsync),
                Entry("Sign-in", "AndroidAutoSignInTemplate (Android Auto)", Symbol("person.badge.key", "document"), () => session.PushAsync(SignInMenu())),
                Entry("Voice control", "CarVoiceControlTemplate", Symbol("mic", "message"), context => PresentVoiceAsync(context.CancellationToken)),
                Entry("No fallback", "Template of the other platform without WithFallback", Symbol("exclamationmark.octagon", "alert"), PushWithoutFallbackAsync),
            ]),
        ],
    };

    // A template allowed on the platform but not in the Parking category (CarTemplateNotAllowedException) → an alert
    // with an explanation
    private async Task ShowOrExplainAsync(Func<Task> show, CarText explanation)
    {
        try
        {
            await show();
        }
        catch (CarTemplateNotAllowedException ex)
        {
            SampleDiagnostics.Log(ex.Message);
            await session.PresentAsync(new CarAlertTemplate(explanation) { Actions = [new CarAction("OK") { Style = CarActionStyle.Cancel }] });
        }
    }

    // CarPlay: the Communication category; Android Auto: an explicit fallback, an information pane with the same data
    private Task PushContactAsync() => ShowOrExplainAsync(
        () => session.PushAsync(Contact()),
        new CarText(["Contact requires the Communication category on CarPlay", "Requires Communication"]));

    private CarPlayContactTemplate Contact() => new CarPlayContactTemplate("City Center Parking Office")
    {
        Id = ContactId,
        Subtitle = "Customer service",
        InformativeText = "Open 7:00–22:00",
        Image = Symbol("person.crop.circle.fill", "info"),
        Buttons =
        [
            new CarPlayContactButton(CarPlayContactButtonKind.Call) { OnSelected = _ => Logged($"Call: {Phone} (CallKit: Stage 6)") },
            new CarPlayContactButton(CarPlayContactButtonKind.Message) { PhoneOrEmail = Phone },
            new CarPlayContactButton(CarPlayContactButtonKind.Directions) { OnSelected = _ => session.PushAsync(Details(_parkings[0])) },
        ],
    }.WithFallback(new CarInformationTemplate("City Center Parking Office")
    {
        Items = [new CarInformationItem("Phone", Phone), new CarInformationItem("Hours", "7:00–22:00")],
        Actions = [new CarAction("Directions", _ => session.PushAsync(Details(_parkings[0]))) { Style = CarActionStyle.Primary }],
    });

    private CarListTemplate SignInMenu() => new("Sign-in")
    {
        Id = SignInMenuId,
        Sections =
        [
            new CarSection([
                Entry("PIN code", "PinSignInMethod", Symbol("number", "document"), () => session.PushAsync(PinSignIn())),
                Entry("QR code", "QRCodeSignInMethod (Car App API 4)", Symbol("qrcode", "grid"), () => session.PushAsync(QrSignIn())),
                Entry("Password", "InputSignInMethod (demo: carkit)", Symbol("key", "document"), () => session.PushAsync(PasswordSignIn(error: null))),
                Entry("Google", "ProviderSignInMethod", Symbol("person.crop.circle.badge.checkmark", "info"), () => session.PushAsync(ProviderSignIn())),
            ]),
        ],
    };

    // CarPlay has no sign-in screen: an explicit fallback with instructions (UseFallbackOrThrow)
    private CarInformationTemplate SignInFallback() => new("Sign-in")
    {
        Items = [new CarInformationItem(null, "Sign in using the CarKit Sample app on your phone.")],
        Actions = [new CarAction("Back", _ => session.PopAsync()) { Style = CarActionStyle.Primary }],
    };

    private AndroidAutoSignInTemplate PinSignIn() => new AndroidAutoSignInTemplate("Sign in", new AndroidAutoPinSignIn("4711-08"))
    {
        Id = PinSignInId,
        Instructions = "Go to carkit.example/tv and enter the code",
        Actions = [new CarAction("Done", _ => SignedInAsync()) { Style = CarActionStyle.Primary }],
    }.WithFallback(SignInFallback());

    private AndroidAutoSignInTemplate QrSignIn() => new AndroidAutoSignInTemplate("Sign in", new AndroidAutoQrCodeSignIn(new Uri("https://carkit.example/tv?code=4711")))
    {
        Id = QrSignInId,
        Instructions = "Scan the code with your phone camera",
        Actions = [new CarAction("Done", _ => SignedInAsync()) { Style = CarActionStyle.Primary }],
    }.WithFallback(SignInFallback());

    // Response to the entered text: an error → UpdateAsync with ErrorMessage (the same screen, no task step), success →
    // a message
    private AndroidAutoSignInTemplate PasswordSignIn(CarText? error) => new AndroidAutoSignInTemplate("Password", new AndroidAutoInputSignIn
    {
        Hint = "Password (demo: carkit)",
        InputType = AndroidAutoInputType.Password,
        ErrorMessage = error,
        OnInputSubmitted = context => context.Text == "carkit"
            ? SignedInAsync()
            : session.UpdateAsync(PasswordSignIn(new CarText(["Wrong password – try \"carkit\"", "Wrong password"]))),
    })
    {
        Id = PasswordSignInId,
        Instructions = "CarKit Sample account",
    }.WithFallback(SignInFallback());

    private AndroidAutoSignInTemplate ProviderSignIn() => new AndroidAutoSignInTemplate(
        "Sign in",
        new AndroidAutoProviderSignIn(new CarAction("Sign in with Google", _ => SignedInAsync()) { Image = Symbol("person.crop.circle", "info") }))
    {
        Id = ProviderSignInId,
        AdditionalText = "Demo: identity provider (Google Sign-In requires the Google SDK in the app)",
    }.WithFallback(SignInFallback());

    private Task SignedInAsync() => session.PushAsync(new CarMessageTemplate("Signed in as Alex.")
    {
        Id = "signed-in",
        Title = "Account",
        Icon = Symbol("checkmark.circle", "message"),
        Actions = [new CarAction("OK", _ => session.PopToRootAsync()) { Style = CarActionStyle.Primary }],
    });

    private static CarVoiceControlTemplate Voice(string active) => new([
        new CarVoiceControlState("listening", new CarText(["Listening…", "Listening"])) { Image = Symbol("mic.fill", "message"), Repeats = true },
        new CarVoiceControlState("thinking", new CarText(["Looking for a free spot…", "Searching…"])) { Image = Symbol("ellipsis.circle", "search") },
        new CarVoiceControlState("speaking", new CarText(["City Center: 42 free spots", "42 free"])) { Image = Symbol("speaker.wave.2.fill", "parking") },
    ])
    {
        Id = VoiceId,
        Title = "Parking assistant",
        ActiveStateId = active,
    };

    // CarPlay: a modal with states (in Parking since iOS 27; with Navigation always); Android Auto: the built-in
    // Message fallback. Conversation simulation: listening → searching → speaking → close, every 2 s while the modal is
    // shown
    private async Task PresentVoiceAsync(CancellationToken cancellationToken)
    {
        var shown = false;
        await ShowOrExplainAsync(
            async () =>
            {
                await session.PresentAsync(Voice("listening"));
                shown = true;
            },
            new CarText(["Voice control in the Parking category requires iOS 27 on CarPlay", "Requires iOS 27"]));
        if (shown)
        {
            _ = SimulateConversationAsync(cancellationToken);
        }
    }

    private async Task SimulateConversationAsync(CancellationToken cancellationToken)
    {
        try
        {
            foreach (var state in new[] { "thinking", "speaking" })
            {
                await Task.Delay(_voiceStep, cancellationToken);
                if (session.Presented?.Id != VoiceId)
                {
                    return;
                }

                await session.UpdateAsync(Voice(state));
            }

            await Task.Delay(_voiceStep, cancellationToken);
            if (session.Presented?.Id == VoiceId)
            {
                await session.DismissAsync();
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or CarKitException or ObjectDisposedException)
        {
            // Disconnect, or the modal was closed by the user during the simulation
            SampleDiagnostics.Log($"Voice: {ex.Message}");
        }
    }

    // A template of the other platform without WithFallback: UseFallbackOrThrow ends with CarTemplateNotSupportedException
    private async Task PushWithoutFallbackAsync()
    {
        CarTemplate template = session.Platform == CarPlatform.CarPlay
            ? new AndroidAutoSignInTemplate("Sign-in", new AndroidAutoPinSignIn("4711")) { Id = "no-fallback" }
            : new CarPlayContactTemplate("City Center Parking Office") { Id = "no-fallback" };
        try
        {
            await session.PushAsync(template);
        }
        catch (CarTemplateNotSupportedException ex)
        {
            SampleDiagnostics.Log(ex.Message);
            await session.PresentAsync(new CarAlertTemplate(new CarText([$"{ex.Kind} does not work on {ex.Platform}", "No template"]))
            {
                Actions = [new CarAction("OK") { Style = CarActionStyle.Cancel }],
            });
        }
    }

    private static Task Logged(string message)
    {
        SampleDiagnostics.Log(message);
        return Task.CompletedTask;
    }
}
