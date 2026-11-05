using CarPlay;
using CoreGraphics;
using UIKit;

namespace CarKit.Maui.Renderers;

// CarVoiceControlTemplate → CPVoiceControlTemplate (modal; spec: "Platform template decisions (Stage 4)"). The states
// are only in the constructor, the active state is switched by ActivateVoiceControlState (in-place update), other
// states = replacement. CPVoiceControlState is immutable: the image only from a source available at once, no change
// after a theme change.
// Stage 7: AnimationImages frames → an animated UIImage when all are available at once (otherwise Image).
// Stage 9 (asset net10.0-ios26.4, iOS 26.4): state action buttons → CPVoiceControlState.ActionButtons (a setter, so
// an action change in place) and the modal bar (CPVoiceControlTemplate : ICPBarButtonProviding; the binding marks the
// bar as iOS, the header as iOS 26.4 — hence the explicit IsIOSVersionAtLeast). The 26.0 asset and iOS < 26.4: no
// buttons (the gate removes them from the model).
internal static class VoiceControlTemplateRenderer
{
    // No API for the state image size; the voice image/animation in the center of the CarPlay screen
    private static readonly CGSize _stateImageSize = new(120, 120);

#if IOS26_4_OR_GREATER
    // No API for the voice action button image size; same as the contact buttons
    private static readonly CGSize _actionImageSize = new(40, 40);
#endif

    public static CarPlayTemplateState Render(CarVoiceControlTemplate model, CarPlayRenderContext context)
    {
        var state = new CarPlayTemplateState(model);
        var native = new CPVoiceControlTemplate([.. model.States.Select(voiceState => State(voiceState, state, context))]);
        state.Native = native;
        ApplyButtons(native, model, previous: null, state, context);
        Activate(native, model);
        return state;
    }

    public static void Update(CarPlayTemplateState state, CPVoiceControlTemplate native, CarVoiceControlTemplate model, CarPlayRenderContext context)
    {
        var previous = (CarVoiceControlTemplate)state.Model;
        ApplyButtons(native, model, previous, state, context);
        if (ActiveId(previous) != ActiveId(model))
        {
            Activate(native, model);
        }
    }

    // Stage 9: state action buttons and the modal bar (iOS 26.4)
    private static void ApplyButtons(CPVoiceControlTemplate native, CarVoiceControlTemplate model, CarVoiceControlTemplate? previous, CarPlayTemplateState state, CarPlayRenderContext context)
    {
#if IOS26_4_OR_GREATER
        if (!OperatingSystem.IsIOSVersionAtLeast(26, 4))
        {
            return;
        }

        foreach (var (nativeState, modelState) in native.VoiceControlStates.Zip(model.States))
        {
            if (previous is null || previous.States.FirstOrDefault(s => s.Identifier == modelState.Identifier)?.Actions != modelState.Actions || state.Restyled(context))
            {
                ApplyActions(nativeState, modelState, state, context);
            }
        }

        BarButtonRenderer.Apply(native, model, previous, state, context);
#endif
    }

#if IOS26_4_OR_GREATER
    [System.Runtime.Versioning.SupportedOSPlatform("ios26.4")]
    [System.Runtime.Versioning.UnsupportedOSPlatform("maccatalyst")]
    private static void ApplyActions(CPVoiceControlState native, CarVoiceControlState model, CarPlayTemplateState state, CarPlayRenderContext context)
    {
        native.ActionButtons = CarPlayButtons.FromActions(
            model.Actions, (int)CPVoiceControlState.MaximumActionButtonCount, "voice-action:" + model.Identifier, _actionImageSize, state, context,
            rebuild: () => ApplyActions(native, CurrentState(state, model.Identifier) ?? model, state, context));
    }

    // A late button image: buttons from the current version of the state (an update may have changed the actions)
    private static CarVoiceControlState? CurrentState(CarPlayTemplateState state, string identifier) =>
        ((CarVoiceControlTemplate)state.Model).States.FirstOrDefault(s => s.Identifier == identifier);
#endif

    // No ActiveStateId → the first state (the validator reports an unknown Id)
    private static string? ActiveId(CarVoiceControlTemplate model) =>
        model.ActiveStateId ?? (model.States.IsEmpty ? null : model.States[0].Identifier);

    private static void Activate(CPVoiceControlTemplate native, CarVoiceControlTemplate model)
    {
        if (ActiveId(model) is { } id)
        {
            native.ActivateVoiceControlState(id);
        }
    }

    private static CPVoiceControlState State(CarVoiceControlState model, CarPlayTemplateState state, CarPlayRenderContext context)
    {
        return new CPVoiceControlState(model.Identifier, [.. model.Title.Variants], Image(model, state, context)!, model.Repeats);
    }

    // A late image has nowhere to go (the state is immutable); it stays in the cache for the next show
    private static UIImage? Image(CarVoiceControlState model, CarPlayTemplateState state, CarPlayRenderContext context)
    {
        if (!model.AnimationImages.IsEmpty)
        {
            var frames = model.AnimationImages
                .Select((frame, index) => context.Image(state, $"voice:{model.Identifier}:{index}", frame, _stateImageSize, _ => { }))
                .ToArray();
            if (Array.TrueForAll(frames, frame => frame is not null))
            {
                return UIImage.CreateAnimatedImage(frames!, model.AnimationDuration.TotalSeconds);
            }
        }

        return context.Image(state, "voice:" + model.Identifier, model.Image, _stateImageSize, _ => { });
    }
}
