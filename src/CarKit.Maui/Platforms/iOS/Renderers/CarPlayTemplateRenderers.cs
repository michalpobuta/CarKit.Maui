using CarPlay;
using Foundation;

namespace CarKit.Maui.Renderers;

// Model → CPTemplate and in-place updates according to CarPlayUpdatePlanner (spec: "Template decisions (Stage 3)").
// After every render and every in-place update, the ConfigureCarPlay hooks (Stage 4); beforeHooks: what the parent
// sets on the child object (a tab's TabInfo) before the hook receives the object. Module templates (Stage 5: Now
// Playing) are rendered by the module renderers from the context; without a renderer (map: Stage 8) →
// NotSupportedException, which the session wraps in CarPlatformException. POI (Stage 7): CPPointOfInterestTemplate.
internal static class CarPlayTemplateRenderers
{
    public static CarPlayTemplateState Render(CarTemplate template, CarPlayRenderContext context, Action<CarPlayTemplateState>? beforeHooks = null)
    {
        var state = template switch
        {
            CarListTemplate list => ListTemplateRenderer.Render(list, context),
            CarGridTemplate grid => GridTemplateRenderer.Render(grid, context),
            CarTabTemplate tabs => TabTemplateRenderer.Render(tabs, context),
            CarInformationTemplate information => InformationTemplateRenderer.Render(information, context),
            CarSearchTemplate search => SearchTemplateRenderer.Render(search, context),
            CarMessageTemplate message => MessageTemplateRenderer.Render(message, context),
            CarAlertTemplate alert => AlertTemplateRenderer.Render(alert, context),
            CarActionSheetTemplate sheet => ActionSheetTemplateRenderer.Render(sheet, context),
            CarVoiceControlTemplate voice => VoiceControlTemplateRenderer.Render(voice, context),
            CarPlayContactTemplate contact => ContactTemplateRenderer.Render(contact, context),
            CarPointOfInterestTemplate points => PointOfInterestTemplateRenderer.Render(points, context),
#pragma warning disable CARKIT0001 // Stage 9: the iOS 27 video list (experimental) = CPListTemplate with a header
            CarPlayVideoBrowseTemplate video => ListTemplateRenderer.Render(video, context),
#pragma warning restore CARKIT0001
            _ => context.Module(template)?.Render(template, context)
                ?? throw new NotSupportedException($"CarKit does not render {template.Kind} on CarPlay."),
        };
        state.Native.UserInfo = new NSString(template.Id);
        state.ImageStyle = context.Style;
        beforeHooks?.Invoke(state);
        CarPlayHooks.Apply(template, state.Native, context);
        return state;
    }

    // InPlace: the native object changed and the state has the new model. Replace: nothing changed, the host must
    // replace the template.
    // keepFixed: the host cannot replace (the template is deep in the stack) — an in-place update without the list
    // title/pane layout
    public static CarPlayUpdate Update(
        CarPlayTemplateState state, CarTemplate updated, CarPlayRenderContext context, bool keepFixed = false, Action<CarPlayTemplateState>? beforeHooks = null)
    {
        if (state.Model.GetType() != updated.GetType()
            || (CarPlayUpdatePlanner.Plan(state.Model, updated) == CarPlayUpdate.Replace && !(keepFixed && state.Native is CPListTemplate or CPInformationTemplate)))
        {
            return CarPlayUpdate.Replace;
        }

        switch (state.Native, updated)
        {
            case (CPListTemplate native, CarListTemplate model):
                ListTemplateRenderer.Update(state, native, model, context);
                break;
#pragma warning disable CARKIT0001
            case (CPListTemplate native, CarPlayVideoBrowseTemplate model):
                ListTemplateRenderer.Update(state, native, model, context);
                break;
#pragma warning restore CARKIT0001
            case (CPGridTemplate native, CarGridTemplate model):
                GridTemplateRenderer.Update(state, native, model, context);
                break;
            case (CPTabBarTemplate native, CarTabTemplate model):
                TabTemplateRenderer.Update(state, native, model, context);
                break;
            case (CPInformationTemplate native, CarInformationTemplate model):
                InformationTemplateRenderer.Update(state, native, model, context);
                break;
            case (CPInformationTemplate native, CarMessageTemplate model):
                MessageTemplateRenderer.Update(state, native, model, context);
                break;
            case (CPSearchTemplate, CarSearchTemplate model):
                SearchTemplateRenderer.Update(state, model);
                break;
            case (CPVoiceControlTemplate native, CarVoiceControlTemplate model):
                VoiceControlTemplateRenderer.Update(state, native, model, context);
                break;
            case (CPContactTemplate native, CarPlayContactTemplate model):
                ContactTemplateRenderer.Update(state, native, model, context);
                break;
            case (CPPointOfInterestTemplate native, CarPointOfInterestTemplate model):
                PointOfInterestTemplateRenderer.Update(state, native, model, context);
                break;
            case (CPAlertTemplate or CPActionSheetTemplate, _):
                // The same texts: the action handlers read the current model from the state
                break;
            default:
                if (context.Module(updated) is not { } module || !module.UpdateInPlace(state, updated, context))
                {
                    return CarPlayUpdate.Replace;
                }

                break;
        }

        state.Native.UserInfo = new NSString(updated.Id);
        state.Commit(updated, context.Style);
        // An in-place update may have recreated what the hook changed (e.g. CPBarButton), so the hooks run again
        beforeHooks?.Invoke(state);
        CarPlayHooks.Apply(updated, state.Native, context);
        return CarPlayUpdate.InPlace;
    }
}
