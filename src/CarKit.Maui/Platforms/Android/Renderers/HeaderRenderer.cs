using AndroidX.Car.App.Model;
using NativeAction = AndroidX.Car.App.Model.Action;

namespace CarKit.Maui.Renderers;

// Template header: Header (Car App API 7) with a start action (app icon at the root, Back higher up) and end actions
// from TrailingBarButtons (max. 2); on older hosts the deprecated SetTitle/SetHeaderAction/ActionStrip
internal static class HeaderRenderer
{
    public const int HeaderApiLevel = 7;

    // Action.Builder.setEnabled has @RequiresCarApi(5)
    public const int ActionEnabledApiLevel = 5;

    private const int MaxHeaderActions = 2;

    public static bool UsesHeader(CarAppRenderContext context) => context.ApiLevel >= HeaderApiLevel;

    public static NativeAction Start(CarAppScreenState screen) => (screen.IsRoot ? NativeAction.AppIcon : NativeAction.Back)!;

    public static Header Header(string? title, CarTemplate model, CarAppScreenState screen, CarAppRenderContext context)
    {
        var header = new Header.Builder().SetStartHeaderAction(Start(screen))!;
        if (!string.IsNullOrEmpty(title))
        {
            header.SetTitle(title);
        }

        foreach (var action in EndActions(model, screen, context))
        {
            header.AddEndHeaderAction(action);
        }

        return header.Build()!;
    }

    // ActionStrip (API < 7) accepts at most one action with a title (ACTIONS_CONSTRAINTS_SIMPLE); further titled ones
    // are skipped
    public static ActionStrip? Strip(CarTemplate model, CarAppScreenState screen, CarAppRenderContext context)
    {
        var actions = EndActions(model, screen, context, maxTitled: 1).ToList();
        if (actions.Count == 0)
        {
            return null;
        }

        var strip = new ActionStrip.Builder();
        foreach (var action in actions)
        {
            strip.AddAction(action);
        }

        return strip.Build();
    }

    // An action in the template content; on a modal screen it closes the screen before the handler (screen state read at
    // click time). parkedOnly: parked-only templates (LongMessageTemplate, SignInTemplate) require ParkedOnlyOnClickListener
    // in content actions; icon: only the sign-in provider action (ProviderSignInMethod); in other content actions CarKit
    // shows no icons
    public static NativeAction Action(CarAction model, CarAppRenderContext context, CarAppScreenState screen, bool parkedOnly = false, CarIcon? icon = null)
    {
        var listener = screen.IsModal ? context.ModalAction(() => screen.CloseModal, model) : context.Click(model, model.OnSelected);
        var action = new NativeAction.Builder()
            .SetTitle(CarAppTemplateRenderers.Text(model.Title))!
            .SetOnClickListener(parkedOnly ? ParkedOnlyOnClickListener.Create(listener)! : listener)!;
        if (model.Style == CarActionStyle.Primary)
        {
            action.SetFlags(NativeAction.FlagPrimary);
        }

        if (icon is not null)
        {
            action.SetIcon(icon);
        }

        if (context.ApiLevel >= ActionEnabledApiLevel)
        {
            action.SetEnabled(model.IsEnabled);
        }

        return action.Build()!;
    }

    // A button without a title whose icon is still loading (or failed to load) is skipped: an action without a title and
    // icon is rejected by Car App Library; it returns on the rebuild after the image loads
    private static IEnumerable<NativeAction> EndActions(CarTemplate model, CarAppScreenState screen, CarAppRenderContext context, int maxTitled = int.MaxValue)
    {
        var titled = 0;
        foreach (var button in model.TrailingBarButtons.Take(MaxHeaderActions))
        {
            var icon = context.Icon(screen, button.Image, CarAppImages.SmallPixels);
            if ((button.Title is null && icon is null) || (button.Title is not null && ++titled > maxTitled))
            {
                continue;
            }

            var action = new NativeAction.Builder().SetOnClickListener(context.Click(button, button.OnSelected))!;
            if (button.Title is { } title)
            {
                action.SetTitle(CarAppTemplateRenderers.Text(title));
            }

            if (icon is not null)
            {
                action.SetIcon(icon);
            }

            if (context.ApiLevel >= ActionEnabledApiLevel)
            {
                action.SetEnabled(button.IsEnabled);
            }

            yield return action.Build()!;
        }
    }
}
