namespace CarKit.Maui;

// Built-in fallbacks from the template catalog; they keep the Id (validator), tab and bar buttons of the original, but
// not the hooks (a different native type)
internal static class CarTemplateFallbacks
{
    public static CarTemplate? Create(CarTemplate template) => CreateCore(template) is { } fallback
        ? fallback with
        {
            TabInfo = template.TabInfo,
            LeadingBarButtons = template.LeadingBarButtons,
            TrailingBarButtons = template.TrailingBarButtons,
        }
        : null;

    private static CarTemplate? CreateCore(CarTemplate template) => template switch
    {
        CarActionSheetTemplate sheet => new CarMessageTemplate(sheet.Message ?? sheet.Title ?? string.Empty)
        {
            Title = sheet.Title,
            Actions = sheet.Actions,
        },
        CarAlertTemplate alert => new CarMessageTemplate(alert.Subtitle ?? alert.Title ?? string.Empty)
        {
            Title = alert.Title,
            Icon = alert.Icon,
            Actions = alert.Actions,
        },
        CarVoiceControlTemplate voice => FromVoiceControl(voice),
        CarLongMessageTemplate longMessage => new CarInformationTemplate(longMessage.Title ?? string.Empty)
        {
            Items = [new CarInformationItem(null, longMessage.Message)],
            Actions = longMessage.Actions,
        },
        _ => null,
    };

    private static CarMessageTemplate FromVoiceControl(CarVoiceControlTemplate voice)
    {
        var state = voice.States.FirstOrDefault(s => s.Identifier == voice.ActiveStateId) ?? (voice.States.IsEmpty ? null : voice.States[0]);
        // Stage 7: without a state image, the icon is the first animation frame
        var icon = state?.Image ?? (state is { AnimationImages.IsEmpty: false } ? state.AnimationImages[0] : null);
        // Stage 9: active state action buttons (CarPlay iOS 26.4) as message actions (max. 2 from the Message rules)
        return new CarMessageTemplate(state?.Title ?? string.Empty) { Title = voice.Title, Icon = icon, Actions = state?.Actions ?? [] };
    }
}
