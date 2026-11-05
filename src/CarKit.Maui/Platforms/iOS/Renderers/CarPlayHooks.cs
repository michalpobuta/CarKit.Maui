using CarPlay;

namespace CarKit.Maui.Renderers;

// CarPlay escape hatch (spec: "Platform template decisions (Stage 4)"): every delegate in the invocation list whose
// type accepts the native object (Action<CPListTemplate>, and through contravariance also Action<CPTemplate>),
// without reflection. A hook exception → ICarErrorHandler; the other hooks and showing the template continue.
internal static class CarPlayHooks
{
    public const string Operation = "ConfigureCarPlay";

    public static void Apply(CarTemplate model, CPTemplate native, CarPlayRenderContext context)
    {
        if (model.PlatformHooks.CarPlay is not { } hooks)
        {
            return;
        }

        foreach (var hook in hooks.GetInvocationList())
        {
            try
            {
                if (!Invoke(hook, native))
                {
                    throw new InvalidOperationException(
                        $"A CarPlay hook of type {hook.GetType().Name} cannot configure {native.GetType().Name}; use ConfigureCarPlay for {model.Kind}.");
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                context.Errors.HandleError(ex, new CarErrorContext(Operation, model));
            }
        }
    }

    private static bool Invoke(Delegate hook, CPTemplate native) => native switch
    {
        CPListTemplate list => Invoke(hook, list),
        CPGridTemplate grid => Invoke(hook, grid),
        CPTabBarTemplate tabs => Invoke(hook, tabs),
        CPInformationTemplate information => Invoke(hook, information),
        CPSearchTemplate search => Invoke(hook, search),
        CPAlertTemplate alert => Invoke(hook, alert),
        CPActionSheetTemplate sheet => Invoke(hook, sheet),
        CPVoiceControlTemplate voice => Invoke(hook, voice),
        CPContactTemplate contact => Invoke(hook, contact),
        CPPointOfInterestTemplate points => Invoke(hook, points),
        CPNowPlayingTemplate nowPlaying => Invoke(hook, nowPlaying),
        CPMapTemplate map => Invoke(hook, map),
        _ => Invoke(hook, native),
    };

    private static bool Invoke<TNative>(Delegate hook, TNative native)
        where TNative : CPTemplate
    {
        if (hook is not Action<TNative> typed)
        {
            return false;
        }

        typed(native);
        return true;
    }
}
