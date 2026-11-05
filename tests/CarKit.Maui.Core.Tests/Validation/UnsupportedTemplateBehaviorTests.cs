using CarKit.Maui.Core.Tests.Session;
using CarKit.Maui.Testing;
using Xunit;

namespace CarKit.Maui.Core.Tests.Validation;

// Stage 4 criterion: a test of every UnsupportedTemplateBehavior: a template with a fallback (built-in and explicit),
// without a fallback, a single-platform template on the other platform and a template below the minimum version
// (also the QR sign-in variant)
public class UnsupportedTemplateBehaviorTests
{
    public static TheoryData<UnsupportedTemplateBehavior> Behaviors =>
        [UnsupportedTemplateBehavior.UseFallbackOrThrow, UnsupportedTemplateBehavior.Throw];

    private static CarTemplateValidator Validator(UnsupportedTemplateBehavior behavior, CarAppCategory categories, CarPlatform platform, Version? ios = null, int? apiLevel = null) =>
        V.Create(categories, platform, ios, apiLevel, configure: o => o.UnsupportedTemplateBehavior = behavior);

    private static void AssertNotSupported(Func<CarTemplate> prepare, CarTemplateKind kind, CarPlatform platform)
    {
        var ex = Assert.Throws<CarTemplateNotSupportedException>(() => prepare());
        Assert.Equal(kind, ex.Kind);
        Assert.Equal(platform, ex.Platform);
    }

    private static AndroidAutoSignInTemplate SignIn(AndroidAutoSignInMethod? method = null) =>
        new("Sign-in", method ?? new AndroidAutoPinSignIn("1234")) { Id = "sign-in" };

    private static async Task<(FakeCarHost Host, ICarSession Session)> ConnectAsync(
        UnsupportedTemplateBehavior behavior, CarAppCategory categories, CarPlatform platform)
    {
        var host = new FakeCarHost(categories, platform);
        host.Options.UnsupportedTemplateBehavior = behavior;
        var session = await host.ConnectAsync(new TestHandler((s, _) => s.SetRootAsync(new CarListTemplate("Start") { Id = "root" })));
        return (host, session);
    }

    [Theory]
    [MemberData(nameof(Behaviors))]
    public void Template_with_a_fallback(UnsupportedTemplateBehavior behavior)
    {
        var validator = Validator(behavior, CarAppCategory.Communication, CarPlatform.AndroidAuto);
        var sheet = new CarActionSheetTemplate("Parking", "What to do?") { Actions = [new CarAction("Delete")] };
        var contact = new CarPlayContactTemplate("Anna").WithFallback(new CarInformationTemplate("Anna"));

        if (behavior == UnsupportedTemplateBehavior.Throw)
        {
            AssertNotSupported(() => validator.Prepare(sheet, null), CarTemplateKind.ActionSheet, CarPlatform.AndroidAuto);
            AssertNotSupported(() => validator.Prepare(contact, 2), CarTemplateKind.CarPlayContact, CarPlatform.AndroidAuto);
            return;
        }

        Assert.Equal(sheet.Id, Assert.IsType<CarMessageTemplate>(validator.Prepare(sheet, null)).Id);
        Assert.Equal(contact.Id, Assert.IsType<CarInformationTemplate>(validator.Prepare(contact, 2)).Id);
    }

    [Theory]
    [MemberData(nameof(Behaviors))]
    public void Template_without_a_fallback(UnsupportedTemplateBehavior behavior)
    {
        AssertNotSupported(
            () => Validator(behavior, CarAppCategory.Communication, CarPlatform.AndroidAuto).Prepare(new CarPlayContactTemplate("Anna"), 2),
            CarTemplateKind.CarPlayContact,
            CarPlatform.AndroidAuto);
        AssertNotSupported(
            () => Validator(behavior, CarAppCategory.Parking, CarPlatform.CarPlay).Prepare(SignIn(), 2),
            CarTemplateKind.AndroidAutoSignIn,
            CarPlatform.CarPlay);
    }

    [Theory]
    [MemberData(nameof(Behaviors))]
    public async Task Platform_only_template_on_the_other_platform(UnsupportedTemplateBehavior behavior)
    {
        var (host, session) = await ConnectAsync(behavior, CarAppCategory.Parking, CarPlatform.CarPlay);
        using var _ = host;
        var signIn = SignIn().WithFallback(new CarInformationTemplate("Sign in on your phone"));

        if (behavior == UnsupportedTemplateBehavior.Throw)
        {
            var ex = await Assert.ThrowsAsync<CarTemplateNotSupportedException>(() => session.PushAsync(signIn));
            Assert.Equal(CarTemplateKind.AndroidAutoSignIn, ex.Kind);
            Assert.Equal("root", Assert.Single(session.Stack).Id);
            Assert.DoesNotContain(host.Calls, c => c.Operation == FakeCarHostOperation.Push);
            return;
        }

        await session.PushAsync(signIn);

        var sent = Assert.Single(host.Calls, c => c.Operation == FakeCarHostOperation.Push).Template;
        Assert.Equal(signIn.Id, Assert.IsType<CarInformationTemplate>(sent).Id);
        Assert.Same(signIn, session.Stack[^1]);
        Assert.Empty(host.Errors);
    }

    [Theory]
    [MemberData(nameof(Behaviors))]
    public async Task Contact_on_android_auto_uses_its_fallback_or_throws(UnsupportedTemplateBehavior behavior)
    {
        var (host, session) = await ConnectAsync(behavior, CarAppCategory.Communication, CarPlatform.AndroidAuto);
        using var _ = host;
        var contact = new CarPlayContactTemplate("Anna") { Id = "anna" }.WithFallback(new CarInformationTemplate("Anna"));

        if (behavior == UnsupportedTemplateBehavior.Throw)
        {
            await Assert.ThrowsAsync<CarTemplateNotSupportedException>(() => session.PushAsync(contact));
            Assert.Single(session.Stack);
            return;
        }

        await session.PushAsync(contact);

        Assert.IsType<CarInformationTemplate>(host.Calls[^1].Template);
        Assert.Equal("anna", session.Stack[^1].Id);
    }

    [Theory]
    [MemberData(nameof(Behaviors))]
    public void Version_gated_template_below_its_minimum(UnsupportedTemplateBehavior behavior)
    {
        var alert = new CarAlertTemplate("Attention") { Subtitle = "Gate open" };
        var tabs = new CarTabTemplate([new CarListTemplate("A") { TabInfo = new CarTabInfo("A", CarImage.FromSystemSymbol("a", "a")) }]);
        var tabsWithFallback = tabs.WithFallback(new CarListTemplate("Substitute"));
        var api4 = Validator(behavior, CarAppCategory.Iot, CarPlatform.AndroidAuto, apiLevel: 4);
        var api5 = Validator(behavior, CarAppCategory.Iot, CarPlatform.AndroidAuto, apiLevel: 5);

        AssertNotSupported(() => api5.Prepare(tabs, 1), CarTemplateKind.Tab, CarPlatform.AndroidAuto);
        AssertNotSupported(
            () => Validator(behavior, CarAppCategory.Communication, CarPlatform.CarPlay, ios: new Version(13, 0)).Prepare(new CarPlayContactTemplate("Anna"), 2),
            CarTemplateKind.CarPlayContact,
            CarPlatform.CarPlay);
        if (behavior == UnsupportedTemplateBehavior.Throw)
        {
            AssertNotSupported(() => api4.Prepare(alert, null), CarTemplateKind.Alert, CarPlatform.AndroidAuto);
            AssertNotSupported(() => api5.Prepare(tabsWithFallback, 1), CarTemplateKind.Tab, CarPlatform.AndroidAuto);
            return;
        }

        Assert.Equal("Gate open", Assert.IsType<CarMessageTemplate>(api4.Prepare(alert, null)).Message.Text);
        Assert.Equal(tabs.Id, Assert.IsType<CarListTemplate>(api5.Prepare(tabsWithFallback, 1)).Id);
    }

    // Review Focus 5: QRCodeSignInMethod has @RequiresCarApi(4); below that, a fallback or an exception instead of a host error
    [Theory]
    [MemberData(nameof(Behaviors))]
    public void Qr_sign_in_below_api_4_uses_the_fallback_or_throws(UnsupportedTemplateBehavior behavior)
    {
        var qr = SignIn(new AndroidAutoQrCodeSignIn(new Uri("https://example.com/tv")));
        var withFallback = qr.WithFallback(SignIn(new AndroidAutoPinSignIn("4711")));
        var api3 = Validator(behavior, CarAppCategory.Parking, CarPlatform.AndroidAuto, apiLevel: 3);

        AssertNotSupported(() => api3.Prepare(qr, 2), CarTemplateKind.AndroidAutoSignIn, CarPlatform.AndroidAuto);
        Assert.Same(qr, Validator(behavior, CarAppCategory.Parking, CarPlatform.AndroidAuto, apiLevel: 4).Prepare(qr, 2));
        var pin = SignIn();
        Assert.Same(pin, Validator(behavior, CarAppCategory.Parking, CarPlatform.AndroidAuto, apiLevel: 2).Prepare(pin, 2));
        if (behavior == UnsupportedTemplateBehavior.Throw)
        {
            AssertNotSupported(() => api3.Prepare(withFallback, 2), CarTemplateKind.AndroidAutoSignIn, CarPlatform.AndroidAuto);
            return;
        }

        var resolved = Assert.IsType<AndroidAutoSignInTemplate>(api3.Prepare(withFallback, 2));
        Assert.IsType<AndroidAutoPinSignIn>(resolved.Method);
        Assert.Equal(qr.Id, resolved.Id);
    }

    [Fact]
    public void Supports_reports_native_support_only()
    {
        static ICarCapabilities Android(int api) => new CarCapabilities(CarPlatform.AndroidAuto, null, api);
        ICarCapabilities carPlay = new CarCapabilities(CarPlatform.CarPlay, new Version(26, 0), null);

        Assert.False(Android(8).Supports<CarVoiceControlTemplate>());
        Assert.True(carPlay.Supports<CarVoiceControlTemplate>());
        Assert.True(Android(2).Supports<AndroidAutoSignInTemplate>());
        Assert.False(Android(1).Supports<AndroidAutoSignInTemplate>());
        Assert.False(carPlay.Supports<AndroidAutoSignInTemplate>());
        Assert.False(Android(8).Supports<CarPlayContactTemplate>());
    }

    // Spec (Stage 4): the built-in fallback is another native type, so no hooks; an explicit fallback has its own
    [Fact]
    public void Fallbacks_drop_hooks_and_explicit_fallbacks_keep_theirs()
    {
        Action<object> hook = _ => { };
        var validator = V.Create(CarAppCategory.Parking, CarPlatform.AndroidAuto);
        var sheet = new CarActionSheetTemplate("A", "B").WithPlatformHook(CarPlatform.AndroidAuto, hook);
        var message = new CarMessageTemplate("Substitute").WithPlatformHook(CarPlatform.AndroidAuto, hook);
        var contact = new CarPlayContactTemplate("Anna").WithPlatformHook(CarPlatform.CarPlay, hook).WithFallback(message);

        Assert.Same(CarPlatformHooks.None, validator.Prepare(sheet, null).PlatformHooks);
        Assert.Same(hook, validator.Prepare(contact, 2).PlatformHooks.AndroidAuto);
    }

    [Fact]
    public async Task Renderer_errors_become_platform_errors()
    {
        var (host, session) = await ConnectAsync(UnsupportedTemplateBehavior.UseFallbackOrThrow, CarAppCategory.Parking, CarPlatform.CarPlay);
        using var _ = host;
        host.FailNextCall(new NotSupportedException("CarKit does not render X on CarPlay."));

        var ex = await Assert.ThrowsAsync<CarPlatformException>(() => session.PushAsync(new CarListTemplate("A")));

        Assert.IsType<NotSupportedException>(ex.InnerException);
        Assert.Single(session.Stack);
    }
}
