namespace CarKit.Maui.Renderers;

// The active Android Auto tab (TabTemplate shows the content of the active tab only): the user's selection holds
// until the app changes SelectedTabId in the model; a nonexistent Id → the first tab
internal static class CarTabSelection
{
    public static string? Active(CarTabTemplate? previous, CarTabTemplate updated, string? userSelected)
    {
        bool Exists(string? id) => id is not null && updated.Tabs.Any(tab => tab.Id == id);

        if (previous is not null && previous.SelectedTabId != updated.SelectedTabId && Exists(updated.SelectedTabId))
        {
            return updated.SelectedTabId;
        }

        if (Exists(userSelected))
        {
            return userSelected;
        }

        if (Exists(updated.SelectedTabId))
        {
            return updated.SelectedTabId;
        }

        return updated.Tabs.IsEmpty ? null : updated.Tabs[0].Id;
    }
}
