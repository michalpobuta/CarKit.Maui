namespace CarKit.Maui.Renderers;

// Refresh rules from the Car App Library javadoc ("Template Restrictions"): a different template type is never a
// refresh; list, sections, grid and pane — the previous one was loading, or the same title, sections (titles) and
// rows (count, titles; a title may change together with the toggle state); messages — title and content;
// search — any change; tabs — the same structure, and with the same active tab its content is a refresh of the
// previous one; sign-in — the previous one was loading, or the same title and method kind (Message of the shape).
// LongMessage and SignIn do not count toward the template limit anyway (CarAppTaskSteps.Cost), so their
// classification does not change the steps.
// Stage 8: NavigationTemplate — any content change (javadoc: "supports any content changes as refreshes");
// MapWithContentTemplate and RoutePreviewNavigationTemplate — the content list rules (the shape carries the title
// and the rows of the route or place list)
internal static class CarAppRefreshRules
{
    public static bool IsRefresh(CarAppTemplateShape previous, CarAppTemplateShape next)
    {
        if (previous.Type != next.Type)
        {
            return false;
        }

        return next.Type switch
        {
            CarAppTemplateTypes.Search => true,
            CarAppTemplateTypes.Navigation => true,
            CarAppTemplateTypes.Message or CarAppTemplateTypes.LongMessage => previous.Title == next.Title && previous.Message == next.Message,
            CarAppTemplateTypes.Tab => previous.IsLoading || SameTabs(previous, next),
            CarAppTemplateTypes.SignIn => previous.IsLoading || (previous.Title == next.Title && previous.Message == next.Message),
            _ => previous.IsLoading || (previous.Title == next.Title && !next.IsLoading && SameSections(previous.Sections, next.Sections)),
        };
    }

    private static bool SameTabs(CarAppTemplateShape previous, CarAppTemplateShape next)
    {
        if (previous.Tabs != next.Tabs)
        {
            return false;
        }

        if (previous.ActiveTab != next.ActiveTab)
        {
            return true;
        }

        return (previous.ActiveContent, next.ActiveContent) switch
        {
            (null, null) => true,
            ({ } before, { } after) => IsRefresh(before, after),
            _ => false,
        };
    }

    private static bool SameSections(CarArray<CarAppSectionShape> previous, CarArray<CarAppSectionShape> next) =>
        previous.Count == next.Count
        && previous.Zip(next).All(pair => pair.First.Title == pair.Second.Title && SameRows(pair.First.Rows, pair.Second.Rows));

    private static bool SameRows(CarArray<CarAppRowShape> previous, CarArray<CarAppRowShape> next) =>
        previous.Count == next.Count
        && previous.Zip(next).All(pair => pair.First.Title == pair.Second.Title
            || (pair.First.Toggle is { } before && pair.Second.Toggle is { } after && before != after));
}
