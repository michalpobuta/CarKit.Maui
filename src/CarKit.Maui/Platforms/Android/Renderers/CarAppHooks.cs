namespace CarKit.Maui.Renderers;

// Android Auto escape hatch (spec: "Platform template decisions (Stage 4)"): hooks get the builder right before
// Build(), on every rebuild. ConfigureAndroidAuto stores an Action<Java.Lang.Object> with the target
// CarAppTypedHook<TBuilder> (the builder type filter), and an Action<TBuilder> from a manual PlatformHooks.Add matches
// through contravariance. A hook exception → ICarErrorHandler; the other hooks and the template build continue.
// Hooks none of which matches the builder → warning.
internal static class CarAppHooks
{
    public const string Operation = "ConfigureAndroidAuto";

    public static void Apply<TBuilder>(CarTemplate model, TBuilder builder, CarAppRenderContext context)
        where TBuilder : Java.Lang.Object
    {
        if (model.PlatformHooks.AndroidAuto is not { } hooks)
        {
            return;
        }

        var applied = false;
        foreach (var hook in hooks.GetInvocationList())
        {
            if (hook is not Action<TBuilder> typed || (hook.Target is ICarAppTypedHook filter && !filter.Accepts(builder)))
            {
                continue;
            }

            applied = true;
            try
            {
                typed(builder);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                context.Errors.HandleError(ex, new CarErrorContext(Operation, model));
            }
        }

        if (!applied)
        {
            context.ReportUnmatchedHooks(model, typeof(TBuilder));
        }
    }
}

// The target of the Action<Java.Lang.Object> delegate stored by ConfigureAndroidAuto: lets us check whether the hook
// matches the builder
internal interface ICarAppTypedHook
{
    bool Accepts(Java.Lang.Object builder);
}

internal sealed class CarAppTypedHook<TBuilder>(Action<TBuilder> configure) : ICarAppTypedHook
    where TBuilder : Java.Lang.Object
{
    public bool Accepts(Java.Lang.Object builder) => builder is TBuilder;

    public void Invoke(Java.Lang.Object builder)
    {
        if (builder is TBuilder typed)
        {
            configure(typed);
        }
    }
}
