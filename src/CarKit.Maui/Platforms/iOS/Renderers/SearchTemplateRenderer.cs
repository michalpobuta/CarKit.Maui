using CarPlay;
using Foundation;

namespace CarKit.Maui.Renderers;

// CarSearchTemplate → CPSearchTemplate with a delegate: text → OnSearchTextChanged → results to the completion,
// selecting a result → its OnSelected (completion after the handler), the "search" button → OnSearchSubmitted.
// Hint, InitialText and ShowKeyboardByDefault have no counterpart in CarPlay.
internal static class SearchTemplateRenderer
{
    public static CarPlayTemplateState Render(CarSearchTemplate model, CarPlayRenderContext context)
    {
        var state = new CarPlayTemplateState(model);
        var native = new CPSearchTemplate();
        var searchDelegate = new SearchDelegate(state, context);
        native.Delegate = searchDelegate;
        state.Delegate = searchDelegate;
        state.Native = native;
        return state;
    }

    // The delegate is already attached to the state and the handlers read the current model; there is nothing native to
    // change
    public static void Update(CarPlayTemplateState state, CarSearchTemplate model)
    {
    }

    private sealed class SearchDelegate(CarPlayTemplateState state, CarPlayRenderContext context) : CPSearchTemplateDelegate
    {
        public override void UpdatedSearchText(CPSearchTemplate searchTemplate, string searchText, CPSearchTemplateDelegateUpdateHandler completionHandler) =>
            UpdateAsync(searchText ?? string.Empty, completionHandler).Forget(context.Errors, "SearchTextChanged");

        public override void SelectedResult(CPSearchTemplate searchTemplate, CPListItem item, Action completionHandler)
        {
            var id = (item.UserInfo as NSString)?.ToString();
            if (id is not null && state.SearchResults.TryGetValue(id, out var result))
            {
                context.Run(result.OnSelected, context.Session.CreateContext(result), "Select", completionHandler);
            }
            else
            {
                completionHandler();
            }
        }

        public override void SearchButtonPressed(CPSearchTemplate searchTemplate)
        {
            var model = (CarSearchTemplate)state.Model;
            context.Run(model.OnSearchSubmitted, new CarSearchContext(context.Session, model, state.Search.Text, context.Session.ConnectionToken), "SearchSubmitted");
        }

        // CarPlay waits for the completion of every query; results of an older query do not overwrite the results map
        // of a newer one
        private async Task UpdateAsync(string text, CPSearchTemplateDelegateUpdateHandler completion)
        {
            var request = state.Search.Begin(text);
            CPListItem[] items = [];
            try
            {
                var results = await context.Session.SearchAsync((CarSearchTemplate)state.Model, text);
                if (state.Search.IsLatest(request))
                {
                    state.SearchResults.Clear();
                    foreach (var result in results)
                    {
                        state.SearchResults[result.Id] = result;
                    }
                }

                items = [.. results.Select(result => ListTemplateRenderer.SearchResult(result, state, context))];
            }
            finally
            {
                completion(items);
            }
        }
    }
}
