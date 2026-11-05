using CarKit.Maui.Testing;
using CarKit.Sample.Car;
using Xunit;

namespace CarKit.Maui.Tests.Sample;

// Sample screens on FakeCarHost (the Parking category from the sample .csproj, Strict validation): every menu entry
// opens its template on both platforms without a validation error; the car screen cannot be viewed here (CarPlay
// Simulator, DHU: manual-verification)
public class SampleScreensTests
{
    public static TheoryData<CarPlatform> Platforms => [CarPlatform.CarPlay, CarPlatform.AndroidAuto];

    // Stage 7: the handler receives the CarKit options (categories decide the menu); the same handler on reconnect
    private static async Task<(FakeCarHost Host, ICarSession Session, SampleCarAppHandler Handler)> ConnectAsync(CarPlatform platform)
    {
        var host = new FakeCarHost(CarAppCategory.Parking, platform);
        host.Options.Validation = CarValidationMode.Strict;
        var handler = new SampleCarAppHandler(host.Options);
        var session = await host.ConnectAsync(handler);
        return (host, session, handler);
    }

    private static CarListItem Entry(ICarSession session, string text) =>
        ((CarListTemplate)session.Stack[^1]).Sections.SelectMany(s => s.Items).OfType<CarListItem>().Single(i => i.Text.Text == text);

    // Main menu → submenu → entry
    private static async Task OpenAsync(FakeCarHost host, ICarSession session, string menu, string entry)
    {
        await host.SelectAsync(Entry(session, menu));
        await host.SelectAsync(Entry(session, entry));
    }

    // Stage 9: the details header of the parking list (CarPlay iOS 26.4), and without this feature (Android Auto, 26.0
    // asset, iOS < 26.4) the same content as a highlighted row; the sample asks
    // ICarCapabilities.Supports(CarFeature.ListDetailsHeader)
    [Theory]
    [InlineData(CarPlatform.CarPlay, true)]
    [InlineData(CarPlatform.CarPlay, false)]
    [InlineData(CarPlatform.AndroidAuto, false)]
    public async Task Parking_list_has_a_details_header_or_a_fallback_row(CarPlatform platform, bool header)
    {
        var host = new FakeCarHost(CarAppCategory.Parking, platform);
        using var _ = host;
        host.Options.Validation = CarValidationMode.Strict;
        if (!header)
        {
            host.Features.Remove(CarFeature.ListDetailsHeader);
        }

        var session = await host.ConnectAsync(new SampleCarAppHandler(host.Options));
        await OpenAsync(host, session, "Lists", "Nearby parking");

        var list = Assert.IsType<CarListTemplate>(session.Stack[^1]);
        if (header)
        {
            var details = Assert.IsType<CarListDetailsHeader>(list.DetailsHeader);
            Assert.Equal("City Center", details.Title?.Text);
            Assert.Equal(["Details"], details.Actions.Select(a => a.Title.Text));
            Assert.Equal("Nearby", list.Sections[0].Header?.Text);
            await host.SelectAsync(details.Actions[0]);
            Assert.Equal(CarTemplateKind.Information, session.Stack[^1].Kind);
        }
        else
        {
            Assert.Null(list.DetailsHeader);
            Assert.Equal("Featured", list.Sections[0].Header?.Text);
            Assert.Equal("City Center", Assert.IsType<CarListItem>(Assert.Single(list.Sections[0].Items)).Text.Text);
        }

        Assert.Empty(host.Errors);
    }

    [Theory]
    [MemberData(nameof(Platforms))]
    public async Task Every_menu_entry_opens_its_template(CarPlatform platform)
    {
        var (host, session, _) = await ConnectAsync(platform);
        using var _ = host;
        (string Menu, string Entry, CarTemplateKind Kind)[] expected =
        [
            ("Lists", "Nearby parking", CarTemplateKind.List),
            ("Lists", "Images", CarTemplateKind.List),
            ("Lists", "List elements", CarTemplateKind.List),
            ("Lists", "List update", CarTemplateKind.List),
            ("Lists", "Empty list", CarTemplateKind.List),
            ("Templates", "Grid", CarTemplateKind.Grid),
            ("Templates", "Tabs", CarTemplateKind.Tab),
            ("Templates", "Information pane", CarTemplateKind.Information),
            // CarPlay: Search only in the Navigation category → an alert with the validator message
            ("Templates", "Search", platform == CarPlatform.CarPlay ? CarTemplateKind.Alert : CarTemplateKind.Search),
            ("Templates", "Message", CarTemplateKind.Message),
            ("Templates", "Long message", CarTemplateKind.LongMessage),
            ("Modals", "Alert", CarTemplateKind.Alert),
            ("Modals", "Action sheet", CarTemplateKind.ActionSheet),
            // Stage 4: CarPlay in Parking: contact (Communication) and voice (Parking since iOS 27) → an alert with the
            // validator message; Android Auto: contact with an explicit fallback (the session stack keeps the
            // original), voice with the built-in one
            ("Platform", "Contact", platform == CarPlatform.CarPlay ? CarTemplateKind.Alert : CarTemplateKind.CarPlayContact),
            ("Platform", "Sign-in", CarTemplateKind.List),
            ("Platform", "Voice control", platform == CarPlatform.CarPlay ? CarTemplateKind.Alert : CarTemplateKind.VoiceControl),
            ("Platform", "No fallback", CarTemplateKind.Alert),
        ];

        foreach (var (menu, entry, kind) in expected)
        {
            await OpenAsync(host, session, menu, entry);

            var shown = session.Presented ?? session.Stack[^1];
            Assert.True(kind == shown.Kind, $"{entry}: {shown.Kind}");
            Assert.Empty(host.Errors);
            await ReturnToMenuAsync(session);
        }

        // Nothing was cut by the platform limits (Android Auto: at least 6 list rows)
        Assert.DoesNotContain(host.Calls, c => c.Template is CarListTemplate list && list.Sections.Sum(s => s.Items.Count) > (platform == CarPlatform.CarPlay ? 12 : 6));
    }

    [Theory]
    [MemberData(nameof(Platforms))]
    public async Task Screens_update_and_chain_modals(CarPlatform platform)
    {
        var (host, session, _) = await ConnectAsync(platform);
        using var _ = host;

        await OpenAsync(host, session, "Lists", "List update");
        var counter = (CarListTemplate)session.Stack[^1];
        await host.SelectAsync(counter.Sections[0].Items[0]);
        Assert.Contains("Selected 1", ((CarListItem)((CarListTemplate)session.Stack[^1]).Sections[0].Items[0]).Detail!.Text, StringComparison.Ordinal);
        await host.SelectAsync(counter.Sections[0].Items[1]);
        Assert.Equal(2, ((CarListTemplate)session.Stack[^1]).Sections[1].Items.Count);

        await ReturnToMenuAsync(session);
        await OpenAsync(host, session, "Lists", "List elements");
        var wifi = ((CarListTemplate)session.Stack[^1]).Sections[0].Items.OfType<CarListItem>().Single(i => i.Toggle is not null);
        await host.ToggleAsync(wifi, isChecked: false);
        Assert.Equal("Wi-Fi: off", ((CarListTemplate)session.Stack[^1]).Sections[0].Items.OfType<CarListItem>().Single(i => i.Toggle is not null).Text.Text);
        var gallery = ((CarListTemplate)session.Stack[^1]).Sections[0].Items.OfType<CarListImageRowItem>().Single();
        await host.SelectImageAsync(gallery, 1);
        Assert.Equal("Image 2 selected", session.Presented?.Title?.Text);
        await session.DismissAsync();

        await ReturnToMenuAsync(session);
        await OpenAsync(host, session, "Modals", "Alert");
        await host.SelectAsync(((CarAlertTemplate)session.Presented!).Actions[0]);
        Assert.Equal("Reserved until 18:30", session.Presented?.Title?.Text);

        await ReturnToMenuAsync(session);
        await OpenAsync(host, session, "Templates", "Tabs");
        var tabs = (CarTabTemplate)session.Stack[0];
        await host.SelectTabAsync(tabs.Tabs[1]);
        await host.SelectAsync(((CarListTemplate)tabs.Tabs[0]).Sections[0].Items[0]);
        Assert.Equal("menu", session.Stack[0].Id);
        Assert.Empty(host.Errors);
    }

    // Stage 4: a platform template on its own platform, a fallback or a message on the other (UseFallbackOrThrow)
    [Theory]
    [MemberData(nameof(Platforms))]
    public async Task Platform_screens_follow_the_platform(CarPlatform platform)
    {
        var (host, session, _) = await ConnectAsync(platform);
        using var _ = host;
        var carPlay = platform == CarPlatform.CarPlay;
        CarTemplate? LastSent(FakeCarHostOperation operation) => host.Calls.LastOrDefault(c => c.Operation == operation)?.Template;

        await OpenAsync(host, session, "Platform", "Contact");
        if (carPlay)
        {
            Assert.Contains("Communication", session.Presented?.Title?.Text, StringComparison.Ordinal);
        }
        else
        {
            Assert.IsType<CarInformationTemplate>(LastSent(FakeCarHostOperation.Push));
        }

        await ReturnToMenuAsync(session);
        await OpenAsync(host, session, "Platform", "Sign-in");
        foreach (var method in new[] { "PIN code", "QR code", "Password", "Google" })
        {
            await host.SelectAsync(Entry(session, method));
            Assert.IsType<AndroidAutoSignInTemplate>(session.Stack[^1]);
            var sent = LastSent(FakeCarHostOperation.Push);
            Assert.True(carPlay ? sent is CarInformationTemplate : sent is AndroidAutoSignInTemplate, $"{method}: {sent?.Kind}");
            await session.PopAsync();
        }

        await ReturnToMenuAsync(session);
        await OpenAsync(host, session, "Platform", "Voice control");
        if (carPlay)
        {
            Assert.Contains("iOS 27", session.Presented?.Title?.Text, StringComparison.Ordinal);
        }
        else
        {
            Assert.Equal("listening", Assert.IsType<CarVoiceControlTemplate>(session.Presented).ActiveStateId);
            Assert.IsType<CarMessageTemplate>(LastSent(FakeCarHostOperation.Present));
        }

        await ReturnToMenuAsync(session);
        await OpenAsync(host, session, "Platform", "No fallback");
        Assert.Contains(carPlay ? "AndroidAutoSignIn" : "CarPlayContact", session.Presented?.Title?.Text, StringComparison.Ordinal);
        Assert.Empty(host.Errors);
    }

    // Password sign-in on Android Auto: a wrong password → the same screen with ErrorMessage, a correct one → a message
    [Fact]
    public async Task Password_sign_in_reports_errors_and_signs_in()
    {
        var (host, session, _) = await ConnectAsync(CarPlatform.AndroidAuto);
        using var _ = host;
        await OpenAsync(host, session, "Platform", "Sign-in");
        await host.SelectAsync(Entry(session, "Password"));

        await host.SubmitInputAsync("wrong");
        var signIn = Assert.IsType<AndroidAutoSignInTemplate>(session.Stack[^1]);
        Assert.NotNull(Assert.IsType<AndroidAutoInputSignIn>(signIn.Method).ErrorMessage);

        await host.SubmitInputAsync("carkit");
        Assert.Contains("Signed in", Assert.IsType<CarMessageTemplate>(session.Stack[^1]).Message.Text, StringComparison.Ordinal);
        Assert.Empty(host.Errors);
    }

    [Fact]
    public async Task Search_returns_matching_parkings_on_android_auto()
    {
        var (host, session, _) = await ConnectAsync(CarPlatform.AndroidAuto);
        using var _ = host;

        await OpenAsync(host, session, "Templates", "Search");
        var results = await host.SearchAsync("center");

        Assert.Equal("City Center", Assert.Single(results).Text.Text);
        Assert.Empty(host.Errors);
    }

    [Theory]
    [MemberData(nameof(Platforms))]
    public async Task Reconnect_restores_the_stack(CarPlatform platform)
    {
        var (host, session, handler) = await ConnectAsync(platform);
        using var _ = host;
        await OpenAsync(host, session, "Lists", "Nearby parking");
        // Stage 9: the "Nearby" section is last (on Android Auto "Featured" comes before it, the list header fallback)
        await host.SelectAsync(((CarListTemplate)session.Stack[^1]).Sections[^1].Items[1]);
        string[] before = [.. session.Stack.Select(t => t.Id)];

        await host.DisconnectAsync();
        var restored = await host.ConnectAsync(handler);

        Assert.Equal(before, restored.Stack.Select(t => t.Id));
        Assert.Equal(4, before.Length);
    }

    private static async Task ReturnToMenuAsync(ICarSession session)
    {
        if (session.Presented is not null)
        {
            await session.DismissAsync();
        }

        if (session.Stack[0].Id != "menu")
        {
            var tabs = (CarTabTemplate)session.Stack[0];
            var back = ((CarListTemplate)tabs.Tabs[0]).Sections[0].Items[0];
            await back.OnSelected!(new FakeContextSession(session).Context(back));
        }
        else if (session.Stack.Count > 1)
        {
            await session.PopToRootAsync();
        }
    }

    // A handler context outside FakeCarHost.SelectAsync (returning from the tabs to the menu)
    private sealed class FakeContextSession(ICarSession session)
    {
        public CarItemContext Context(CarElement element) => ((CarSession)session).CreateContext(element);
    }
}
