using AndroidX.Car.App.Model;

namespace CarKit.Maui.Renderers;

// CarSearchTemplate → SearchTemplate: text → OnSearchTextChanged → results in the screen state and a rebuild (every
// change of SearchTemplate is a refresh), "search" → OnSearchSubmitted. Older results do not overwrite newer ones
// (CarSearchRequests).
internal static class SearchTemplateRenderer
{
    public static CarAppRendered Render(CarSearchTemplate model, CarAppRenderContext context, CarAppScreenState screen)
    {
        var builder = new SearchTemplate.Builder(new SearchCallback(model, context, screen));
        if (!screen.InTab)
        {
            builder.SetHeaderAction(HeaderRenderer.Start(screen));
        }

        if (model.Hint is { } hint)
        {
            builder.SetSearchHint(hint.Text);
        }

        var text = screen.Search.Text.Length > 0 ? screen.Search.Text : model.InitialText;
        if (!string.IsNullOrEmpty(text))
        {
            builder.SetInitialSearchText(text);
        }

        builder.SetShowKeyboardByDefault(model.ShowKeyboardByDefault);
        if (HeaderRenderer.Strip(model, screen, context) is { } strip)
        {
            builder.SetActionStrip(strip);
        }

        var (list, rows) = ListTemplateRenderer.Items(screen.SearchResults, context, screen, allowToggle: false);
        builder.SetItemList(list.Build());
        CarAppHooks.Apply(model, builder, context);
        return new(builder.Build()!, new CarAppTemplateShape(CarAppTemplateTypes.Search, null, IsLoading: false)
        {
            Sections = [new CarAppSectionShape(null, rows)],
        });
    }

    private sealed class SearchCallback(CarSearchTemplate model, CarAppRenderContext context, CarAppScreenState screen)
        : Java.Lang.Object, SearchTemplate.ISearchCallback
    {
        public void OnSearchTextChanged(string? searchText) => UpdateAsync(searchText ?? string.Empty).Forget(context.Errors, "SearchTextChanged");

        public void OnSearchSubmitted(string? searchText) =>
            context.Run(model.OnSearchSubmitted, new CarSearchContext(context.Session, model, searchText ?? string.Empty, context.Session.ConnectionToken), "SearchSubmitted");

        private async Task UpdateAsync(string text)
        {
            var request = screen.Search.Begin(text);
            var results = await context.Session.SearchAsync(model, text);
            if (screen.Search.IsLatest(request))
            {
                screen.SearchResults = results;
                screen.RequestRerender(context.Post);
            }
        }
    }
}
