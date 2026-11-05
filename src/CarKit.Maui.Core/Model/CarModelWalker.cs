namespace CarKit.Maui;

// Walks the model without reflection: renderers and FakeCarHost use it to find elements by Id
internal static class CarModelWalker
{
    public static CarElement? Find(CarTemplate template, string id)
    {
        foreach (var element in Elements(template))
        {
            if (element.Id == id)
            {
                return element;
            }
        }

        return null;
    }

    public static IEnumerable<CarElement> Elements(CarTemplate template)
    {
        foreach (var button in template.LeadingBarButtons)
        {
            yield return button;
        }

        foreach (var button in template.TrailingBarButtons)
        {
            yield return button;
        }

        foreach (var element in OwnElements(template))
        {
            yield return element;
        }
    }

    private static IEnumerable<CarElement> OwnElements(CarTemplate template) => template switch
    {
        CarListTemplate list => HeaderElements(list.DetailsHeader).Concat(SectionElements(list.Sections)),
#pragma warning disable CARKIT0001
        CarPlayVideoBrowseTemplate video => HeaderElements(video.DetailsHeader).Concat(SectionElements(video.Sections)),
#pragma warning restore CARKIT0001
        CarGridTemplate grid => grid.Items,
        CarTabTemplate tabs => tabs.Tabs.SelectMany(tab => Elements(tab).Prepend(tab)),
        CarInformationTemplate information => information.Actions,
        CarPointOfInterestTemplate poi => poi.Points.SelectMany(PointElements),
        CarMessageTemplate message => message.Actions,
        CarLongMessageTemplate longMessage => longMessage.Actions,
        CarAlertTemplate alert => alert.Actions,
        CarActionSheetTemplate sheet => sheet.Actions,
        // Stage 9: voice state action buttons (iOS 26.4)
        CarVoiceControlTemplate voice => voice.States.SelectMany(state => state.Actions),
        CarNowPlayingTemplate nowPlaying => nowPlaying.Buttons,
        CarMapTemplate map => map.MapButtons,
        CarPlayContactTemplate contact => contact.Buttons,
        AndroidAutoSignInTemplate signIn => signIn.Method is AndroidAutoProviderSignIn provider
            ? signIn.Actions.Prepend(provider.Action)
            : signIn.Actions,
        _ => [],
    };

    // Stage 9: list details header buttons (iOS 26.4)
    private static IEnumerable<CarElement> HeaderElements(CarListDetailsHeader? header) =>
        header is null ? Enumerable.Empty<CarElement>() : header.Actions;

    private static IEnumerable<CarElement> SectionElements(CarArray<CarSection> sections) =>
        sections.SelectMany(section => section.Items.Cast<CarElement>().Prepend(section));

    private static IEnumerable<CarElement> PointElements(CarPointOfInterest point)
    {
        yield return point;
        if (point.PrimaryAction is { } primary)
        {
            yield return primary;
        }

        if (point.SecondaryAction is { } secondary)
        {
            yield return secondary;
        }
    }
}
