namespace CarKit.Maui.Renderers;

// Car App Library screen state between rebuilds: the user's tab selection, search and results, the rebuild queue
// after an image loads. The host sets Rerender (a rebuild with the current model = a refresh). Main thread only.
internal sealed class CarAppScreenState
{
    private readonly Dictionary<string, CarAppScreenState> _tabs = [];
    private CarAppScreenState? _parent;
    private bool _queued;

    public bool IsRoot { get; set; }

    public bool IsModal { get; init; }

    // Tab content: without a header (the header belongs to TabTemplate)
    public bool InTab { get; private init; }

    public string? UserSelectedTab { get; set; }

    // The last rendered tabs model (CarTabSelection compares SelectedTabId)
    public CarTabTemplate? RenderedTabs { get; set; }

    public CarSearchRequests Search { get; private set; } = new();

    public IReadOnlyList<CarListItem> SearchResults { get; set; } = [];

    public Action? Rerender { get; set; }

    // A modal shown as a screen (Message fallback): closed before the action handler
    public Func<Task>? CloseModal { get; set; }

    // A new model on the same screen (SetRootAsync with a different Id): the tab selection and search results belonged to
    // the previous one
    public void Reset()
    {
        UserSelectedTab = null;
        RenderedTabs = null;
        Search = new CarSearchRequests();
        SearchResults = [];
        _tabs.Clear();
    }

    public CarAppScreenState Tab(string tabId)
    {
        if (!_tabs.TryGetValue(tabId, out var child))
        {
            child = new CarAppScreenState { InTab = true, _parent = this };
            _tabs[tabId] = child;
        }

        return child;
    }

    // Several images finish at once → one rebuild on the next turn of the main loop
    public void RequestRerender(Action<Action> post)
    {
        if (_parent is { } parent)
        {
            parent.RequestRerender(post);
            return;
        }

        if (_queued || Rerender is null)
        {
            return;
        }

        _queued = true;
        post(() =>
        {
            _queued = false;
            Rerender?.Invoke();
        });
    }
}
