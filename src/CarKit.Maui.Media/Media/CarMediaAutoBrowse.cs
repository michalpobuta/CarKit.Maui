namespace CarKit.Maui.Media;

// media.UseAutoBrowse() (spec: "Media module decisions (Stage 5)"): car screens from the library. A root with only
// folders (2 … tab limit) → tabs with one list per tab, otherwise a single list; folder → push of the children list,
// track → PlayAsync and Now Playing. Paging: a full page ends with a "More" row that appends the next page
// (UpdateAsync; tab list → update of the tab template) up to the list limit. The handler is a singleton and the list
// state lives in the closures of the item handlers. No ConfigureAwait(false): the library and the session are called
// from the main thread (ICarMediaLibrary contract), and the continuations return to it.
internal sealed class CarMediaAutoBrowseHandler(CarMediaRuntime runtime) : ICarAppHandler
{
    private readonly CarMediaRuntime _runtime = runtime;

    public const string MoreText = "More";
    public const string UnavailableText = "Content unavailable";
    public const string UpNextText = "Up Next";
    private const string LibraryText = "Library";

    // A page of at most 20 rows (quick library response), always with room for "More"
    private const int MaxPageSize = 20;

    public async Task OnConnectedAsync(ICarSession session, CancellationToken ct)
    {
        CarMediaItem root;
        try
        {
            root = await _runtime.Library.GetRootAsync(ct);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not OperationCanceledException)
        {
            Report(ex, "root");
            await session.SetRootAsync(Unavailable(LibraryText));
            return;
        }

        var browser = new Browser(this, session);
        var top = await browser.LoadPageAsync(root.Id, 0, ct);
        var tabLimit = session.Capabilities.GetLimit(CarLimitKind.Tabs) ?? int.MaxValue;
        if (top is { } children
            && children.Count >= 2 && children.Count <= tabLimit
            && children.All(c => c.IsBrowsable)
            && session.Capabilities.Supports<CarTabTemplate>())
        {
            var tabs = new List<CarTemplate>(children.Count);
            foreach (var child in children)
            {
                tabs.Add(await browser.CreateListAsync(child, ct, new CarTabInfo(child.Title, child.Artwork)));
            }

            browser.Tabs = new CarTabTemplate(tabs);
            await session.SetRootAsync(browser.Tabs);
            return;
        }

        await session.SetRootAsync(top is null ? Unavailable(root.Title) : browser.CreateList(root, top));
    }

    private static CarListTemplate Unavailable(string title) => new(title) { EmptyViewTitle = UnavailableText };

    private void Report(Exception exception, string parentId)
    {
        CarMediaLog.LibraryFailed(_runtime.Logger, parentId, exception);
        _runtime.Errors.HandleError(exception, new CarErrorContext("Media.Library", null));
    }

    // Browsing within one session: lists, their pages and Now Playing
    private sealed class Browser(CarMediaAutoBrowseHandler owner, ICarSession session)
    {
        private CarMediaRuntime Runtime => owner._runtime;

        // Root with tabs: a tab list is not on the stack, so "More" updates this template
        public CarTabTemplate? Tabs { get; set; }

        private int ListLimit => Math.Max(2, session.Capabilities.GetLimit(CarLimitKind.ListItems) ?? 100);

        private int MaxDepth => session is CarSession car ? car.MaxStackDepth : int.MaxValue;

        private int PageSize => Math.Clamp(ListLimit - 1, 1, MaxPageSize);

        // null = library error (reported)
        public async Task<IReadOnlyList<CarMediaItem>?> LoadPageAsync(string parentId, int page, CancellationToken ct)
        {
            try
            {
                return await Runtime.Library.GetChildrenAsync(parentId, page, PageSize, ct);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not OperationCanceledException)
            {
                owner.Report(ex, parentId);
                return null;
            }
        }

        public async Task<CarListTemplate> CreateListAsync(CarMediaItem parent, CancellationToken ct, CarTabInfo? tab = null) =>
            await LoadPageAsync(parent.Id, 0, ct) is { } items ? CreateList(parent, items, tab) : Unavailable(parent.Title) with { TabInfo = tab };

        public CarListTemplate CreateList(CarMediaItem parent, IReadOnlyList<CarMediaItem> firstPage, CarTabInfo? tab = null)
        {
            var paged = new PagedList(parent, new CarListTemplate(parent.Title) { Id = "carkit-media-" + Guid.NewGuid().ToString("N"), TabInfo = tab });
            paged.Items.AddRange(firstPage);
            paged.Pages = 1;
            paged.HasMore = firstPage.Count >= PageSize;
            return Render(paged);
        }

        private CarListTemplate Render(PagedList paged)
        {
            var rows = new List<CarListTemplateItem>(paged.Items.Count + 1);
            var playing = Runtime.Controller.State is { Status: CarPlaybackStatus.Playing, Item: { } current } ? current.Id : null;
            // Row id = list + position: the same item can be in several tabs and several times in a list,
            // and appended pages do not change the ids of earlier rows (in-place update)
            foreach (var (item, index) in paged.Items.Take(ListLimit).Select((item, index) => (item, index)))
            {
                rows.Add(Row(item, paged.Template.Id + "-" + index, playing));
            }

            if (paged.HasMore && rows.Count < ListLimit)
            {
                rows.Add(new CarListItem(MoreText) { Id = paged.Template.Id + "-more", OnSelected = _ => MoreAsync(paged) });
            }

            paged.Template = paged.Template with { Sections = [new CarSection(rows)] };
            return paged.Template;
        }

        private async Task MoreAsync(PagedList paged)
        {
            var next = await LoadPageAsync(paged.Parent.Id, paged.Pages, CancellationToken.None);
            if (next is null)
            {
                return;
            }

            paged.Items.AddRange(next);
            paged.Pages++;
            paged.HasMore = next.Count >= PageSize && paged.Items.Count < ListLimit;
            var list = Render(paged);
            if (Tabs is { } tabs && tabs.Tabs.Any(t => t.Id == list.Id))
            {
                Tabs = tabs with { Tabs = [.. tabs.Tabs.Select(t => t.Id == list.Id ? list : t)] };
                await session.UpdateAsync(Tabs);
                return;
            }

            await session.UpdateAsync(list);
        }

        private CarListItem Row(CarMediaItem item, string id, string? playingId) => new(item.Title)
        {
            Id = id,
            Detail = item.Subtitle is { } subtitle ? new CarText(subtitle) : null,
            Image = item.Artwork,
            IsBrowsable = item.IsBrowsable,
            IsPlaying = item.IsPlaying || item.Id == playingId,
            PlaybackProgress = item.Progress,
            IsExplicitContent = item.IsExplicit,
            OnSelected = item.IsBrowsable ? _ => OpenAsync(item) : item.IsPlayable ? _ => PlayAsync(item.Id) : null,
        };

        private async Task OpenAsync(CarMediaItem folder) =>
            await session.PushAsync(await CreateListAsync(folder, CancellationToken.None));

        private async Task PlayAsync(string itemId)
        {
            await Runtime.Controller.PlayAsync(itemId);
            await ShowNowPlayingAsync();
        }

        // Now Playing once on the stack: if it is lower → go back to it, if absent → push; a platform without the
        // template (AA < API 8) draws it itself
        private async Task ShowNowPlayingAsync()
        {
            if (session.Stack.OfType<CarNowPlayingTemplate>().FirstOrDefault() is { } shown)
            {
                if (!ReferenceEquals(session.Stack[^1], shown))
                {
                    await session.PopToAsync(shown);
                }

                return;
            }

            // Android Auto without media templates draws playback itself (Media3 service)
            if (!session.Capabilities.Supports<CarNowPlayingTemplate>()
                || (session.Capabilities.Platform != CarPlatform.CarPlay && !Runtime.Options.TemplatedMedia))
            {
                return;
            }

            var nowPlaying = Runtime.Options.NowPlaying;
            if (nowPlaying.IsUpNextButtonEnabled && nowPlaying.OnUpNextSelected is null)
            {
                nowPlaying = nowPlaying with { OnUpNextSelected = _ => ShowQueueAsync(nowPlaying.UpNextTitle?.Text ?? UpNextText) };
            }

            // Depth limit (Audio: 5): Now Playing and its Up Next must fit, otherwise return to the root
            var needed = session.Stack.Count + 1 + (nowPlaying.IsUpNextButtonEnabled ? 1 : 0);
            if (needed > MaxDepth && session.Stack.Count > 1)
            {
                await session.PopToRootAsync(animated: false);
            }

            await session.PushAsync(nowPlaying);
        }

        // Up Next without an app handler: the player queue, selection → playback and return to Now Playing
        private Task ShowQueueAsync(string title)
        {
            var state = Runtime.Controller.State;
            var rows = state.Queue.Take(ListLimit).Select((item, index) => (CarListTemplateItem)new CarListItem(item.Title)
            {
                Id = "carkit-media-queue-" + index,
                Detail = item.Subtitle is { } subtitle ? new CarText(subtitle) : null,
                Image = item.Artwork,
                IsPlaying = index == state.QueueIndex,
                OnSelected = _ => PlayAsync(item.Id),
            });
            return session.PushAsync(new CarListTemplate(title) { Sections = [new CarSection(rows)], EmptyViewTitle = UnavailableText });
        }
    }

    // List with incrementally loaded pages; Template = the last sent model (UpdateAsync with the same Id)
    private sealed class PagedList(CarMediaItem parent, CarListTemplate template)
    {
        public CarMediaItem Parent { get; } = parent;

        public CarListTemplate Template { get; set; } = template;

        public List<CarMediaItem> Items { get; } = [];

        public int Pages { get; set; }

        public bool HasMore { get; set; }
    }
}
