namespace CarKit.Maui.Renderers;

// How CarPlay can show a new version of a template: in place (CP* setters) or only by replacing the object
internal enum CarPlayUpdate
{
    InPlace,
    Replace,
}

// CarPlay update plan (spec: "Template decisions (Stage 3)"). Replacement only where CarPlay has no API: the
// CPListTemplate title, the CPInformationTemplate Layout, the content of the immutable CPAlertTemplate and
// CPActionSheetTemplate, CPVoiceControlTemplate states (only in the constructor; the active state is switched by
// ActivateVoiceControlState, the states' action buttons have a setter from iOS 26.4). List: the details header
// (iOS 26.4) with the ListHeader setter. Contact: Contact with a setter, Now Playing (Media module), POI (Stage 7)
// and the map (Navigation module, Stage 8): always in place.
internal static class CarPlayUpdatePlanner
{
    public static CarPlayUpdate Plan(CarTemplate previous, CarTemplate updated)
    {
        if (previous.GetType() != updated.GetType())
        {
            return CarPlayUpdate.Replace;
        }

        var inPlace = (previous, updated) switch
        {
            (CarListTemplate p, CarListTemplate u) => p.Title?.Text == u.Title?.Text,
#pragma warning disable CARKIT0001 // Stage 9: the video list = CPListTemplate (title read-only)
            (CarPlayVideoBrowseTemplate p, CarPlayVideoBrowseTemplate u) => p.Title?.Text == u.Title?.Text,
#pragma warning restore CARKIT0001
            (CarInformationTemplate p, CarInformationTemplate u) => p.Layout == u.Layout,
            (CarAlertTemplate p, CarAlertTemplate u) => Texts(p.Title) == Texts(u.Title) && SameActions(p.Actions, u.Actions),
            (CarActionSheetTemplate p, CarActionSheetTemplate u) =>
                p.Title?.Text == u.Title?.Text && p.Message?.Text == u.Message?.Text && SameActions(p.Actions, u.Actions),
            // Stage 9: the states' action buttons (ActionButtons, iOS 26.4) have a setter; the iOS 27 overlay is a
            // different presentation
            (CarVoiceControlTemplate p, CarVoiceControlTemplate u) => SameVoiceStates(p.States, u.States) && SameOverlay(p, u),
            (CarGridTemplate, _) or (CarTabTemplate, _) or (CarSearchTemplate, _) or (CarMessageTemplate, _) or (CarPlayContactTemplate, _) => true,
            // Stage 7: CPPointOfInterestTemplate — Title with a setter, the points via SetPointsOfInterest, selection
            // via SelectedIndex
            (CarPointOfInterestTemplate, _) => true,
            // A single CPNowPlayingTemplate.SharedTemplate: the buttons (UpdateNowPlayingButtons), Up Next and
            // Album/Artist have setters
            (CarNowPlayingTemplate, _) => true,
            // Stage 8 (Navigation module): CPMapTemplate — the map buttons, the bar, the panel color and hiding the bar
            // have setters
            (CarMapTemplate, _) => true,
            _ => false,
        };
        return inPlace ? CarPlayUpdate.InPlace : CarPlayUpdate.Replace;
    }

    // The same list structure (sections, headers, index, row Ids and types) → setters of the existing CPListItem
    // instead of UpdateSections; this keeps the spinner and the completion of the selected row on the same object
    public static bool HasSameItems(CarListTemplate previous, CarListTemplate updated)
    {
        if (previous.IsLoading || updated.IsLoading)
        {
            return previous.IsLoading && updated.IsLoading;
        }

        if (previous.Sections.Count != updated.Sections.Count)
        {
            return false;
        }

        for (var s = 0; s < previous.Sections.Count; s++)
        {
            var (before, after) = (previous.Sections[s], updated.Sections[s]);
            if (before.Header?.Text != after.Header?.Text || before.IndexTitle != after.IndexTitle || before.Items.Count != after.Items.Count)
            {
                return false;
            }

            for (var i = 0; i < before.Items.Count; i++)
            {
                if (before.Items[i].Id != after.Items[i].Id || before.Items[i].GetType() != after.Items[i].GetType())
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool SameVoiceStates(CarArray<CarVoiceControlState> previous, CarArray<CarVoiceControlState> updated) =>
        previous.Count == updated.Count
        && previous.Zip(updated).All(pair => pair.First with { Actions = [] } == pair.Second with { Actions = [] });

#pragma warning disable CARKIT0001 // the Voice Control overlay (iOS 27) is experimental
    private static bool SameOverlay(CarVoiceControlTemplate previous, CarVoiceControlTemplate updated) => previous.IsOverlay == updated.IsOverlay;
#pragma warning restore CARKIT0001

    private static string Texts(CarText? text) => text is null ? string.Empty : string.Join('\n', text.Variants);

    // CPAlertAction has only a title and a style (the renderer skips disabled actions); the handler replaces the
    // renderer state
    private static bool SameActions(CarArray<CarAction> previous, CarArray<CarAction> updated) =>
        previous.Count == updated.Count
        && previous.Zip(updated).All(pair =>
            pair.First.Title.Text == pair.Second.Title.Text && pair.First.Style == pair.Second.Style && pair.First.IsEnabled == pair.Second.IsEnabled);
}
