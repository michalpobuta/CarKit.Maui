namespace CarKit.Maui.Renderers;

// Android Auto task steps (spec: "Template decisions (Stage 3)"): the host shows at most 5 templates per task and
// counts templates, not screens; the fifth must end the task (Pane, Message, LongMessage, SignIn, Navigation,
// MediaPlayback). A push = 1 step, a rebuild that is not a refresh = +1 on that screen, removing a screen returns its
// steps. LongMessage and SignIn do not count at all (javadoc: "does not count against the template quota"; Stage 4).
// Stage 8: NavigationTemplate resets the limit ("the host resets the template quota, treating that template as
// though it's the first step of a new task"): the screen with it has 1 step, the others 0.
// The key is the screen object (reference comparison). Main thread only.
internal sealed class CarAppTaskSteps
{
    public const int HostLimit = 5;

    private static readonly HashSet<string> _terminalTypes =
    [
        CarAppTemplateTypes.Pane,
        CarAppTemplateTypes.Message,
        CarAppTemplateTypes.LongMessage,
        CarAppTemplateTypes.SignIn,
        CarAppTemplateTypes.Navigation,
        // Stage 5: the 1.7 javadoc does not exempt MediaPlaybackTemplate from the limit, so it costs a step like any
        // template
        CarAppTemplateTypes.MediaPlayback,
    ];

    // Parked-only templates that the host skips in the limit
    private static readonly HashSet<string> _quotaFreeTypes = [CarAppTemplateTypes.LongMessage, CarAppTemplateTypes.SignIn];

    // The steps a new template of the given type uses (a push or a rebuild that is not a refresh)
    public static int Cost(string templateType) => _quotaFreeTypes.Contains(templateType) ? 0 : 1;

    // resultingTotal: the number of the task's templates after sending a template of type templateType
    public static bool Allows(int resultingTotal, string templateType) =>
        resultingTotal < HostLimit || (resultingTotal == HostLimit && _terminalTypes.Contains(templateType));

    private readonly Dictionary<object, int> _steps = new(ReferenceEqualityComparer.Instance);

    // Screens with a template outside the limit: rebuilds add no steps either
    private readonly HashSet<object> _quotaFree = new(ReferenceEqualityComparer.Instance);

    public int Total => _steps.Values.Sum();

    public int StepsOf(object screen) => _steps.GetValueOrDefault(screen);

    public bool WouldExceed(int additional) => Total + additional > HostLimit;

    public void Pushed(object screen, string templateType)
    {
        if (templateType == CarAppTemplateTypes.Navigation)
        {
            StartTaskAt(screen);
            return;
        }

        _steps[screen] = Cost(templateType);
        if (Cost(templateType) == 0)
        {
            _quotaFree.Add(screen);
        }
        else
        {
            _quotaFree.Remove(screen);
        }
    }

    public void Updated(object screen, bool isRefresh, string templateType)
    {
        if (!isRefresh && templateType == CarAppTemplateTypes.Navigation && _steps.ContainsKey(screen))
        {
            StartTaskAt(screen);
            return;
        }

        Updated(screen, isRefresh);
    }

    public void Updated(object screen, bool isRefresh)
    {
        if (!isRefresh && !_quotaFree.Contains(screen) && _steps.TryGetValue(screen, out var steps))
        {
            _steps[screen] = steps + 1;
        }
    }

    public void Removed(object screen)
    {
        _steps.Remove(screen);
        _quotaFree.Remove(screen);
    }

    public void ResetTo(object root, string templateType)
    {
        _steps.Clear();
        _quotaFree.Clear();
        Pushed(root, templateType);
    }

    // Screens below (and possibly above) NavigationTemplate stay on the stack but no longer use steps
    private void StartTaskAt(object screen)
    {
        foreach (var key in _steps.Keys.ToList())
        {
            _steps[key] = 0;
        }

        _steps[screen] = 1;
        _quotaFree.Remove(screen);
    }
}

internal enum CarAppRootPlan
{
    // PopToRoot and a rebuild of the root screen
    RefreshRoot,

    // A new root screen (push), then ScreenManager.Remove of the old ones
    ReplaceRoot,
}

internal static class CarAppRootPlans
{
    // The host treats a return to a screen that will send a different template type than before as an error. Also
    // without screens above: a pop right before SetRootAsync leaves a pending return on the root (ScreenManager:
    // useLastTemplateId), so every change of the root's type is a new screen
    public static CarAppRootPlan For(string currentRootType, string newRootType) =>
        currentRootType != newRootType ? CarAppRootPlan.ReplaceRoot : CarAppRootPlan.RefreshRoot;
}

// Stage 8 / I2 (review): after the screens above a screen are removed, ScreenManager asks it for a template with the
// previous identifier (setUseLastTemplateId), and the host treats a type different from the last one sent as an error
// ("when popping back to a screen, an app must send a template that is of the same type as the one last sent by that
// screen"). A screen whose type changed while it was obscured (e.g. map: guidance → route preview after PopToRoot)
// first sends the previous template, and the new one in the next frame (Invalidate), as an ordinary template change
// on top.
internal static class CarAppScreenReturns
{
    public static bool ResendLast(string? lastSentType, string currentType, bool returning) =>
        returning && lastSentType is not null && lastSentType != currentType;
}
