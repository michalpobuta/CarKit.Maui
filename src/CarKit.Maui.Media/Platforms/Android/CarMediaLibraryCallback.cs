using Android.Content;
using Android.OS;
using Android.Runtime;
using AndroidX.Media3.Common;
using AndroidX.Media3.Session;
using Google.Common.Util.Concurrent;
using static AndroidX.Media3.Session.MediaLibraryService;

namespace CarKit.Maui.Media;

// ICarMediaLibrary as a Media3 library (spec: "Media module decisions (Stage 5)"): root, children with paging, item,
// search (OnSearch → result memory per controller → OnGetSearchResult), session buttons from media.NowPlaying
// (carkit.* commands), items from setMediaItems unchanged. Media3 calls on the main loop; the result goes through a
// future from a pool thread. All interface methods are implemented: default methods of a Java interface in the binding
// called through ACW end with AbstractMethodError (device test, onPostConnect), so the default Media3 behavior is
// recreated here explicitly.
internal sealed class CarMediaLibraryCallback(CarMediaRuntime runtime, Context context) : Java.Lang.Object, MediaLibrarySession.ICallback
{
    private readonly CarMediaSearchCache _searches = new();
    private readonly HashSet<string> _skippedButtons = new(StringComparer.Ordinal);

    public CancellationToken Cancellation { get; set; }

    public MediaSession.ConnectionResult? OnConnect(MediaSession? session, MediaSession.ControllerInfo? controller)
    {
        var commands = MediaSession.ConnectionResult.DefaultSessionAndLibraryCommands!.BuildUpon()!;
        foreach (var button in CarNowPlayingButtons.Resolve(runtime.Options.NowPlaying, runtime.Controller.State))
        {
            commands.Add(new SessionCommand(button.CommandId, Bundle.Empty!));
        }

        return new MediaSession.ConnectionResult.AcceptedResultBuilder(session!)
            .SetAvailableSessionCommands(commands.Build()!)!
            .SetMediaButtonPreferences(Buttons(runtime.Controller.State))!
            .Build();
    }

    public IListenableFuture? OnGetLibraryRoot(MediaLibrarySession? session, MediaSession.ControllerInfo? browser, LibraryParams? @params) =>
        CarMediaFutures.From(
            runtime.Library.GetRootAsync(Cancellation),
            root =>
            {
                var extras = new Bundle();
                extras.PutBoolean(CarMediaItems.SearchSupportedKey, true);
                return LibraryResult.OfItem(CarMediaItems.ToMediaItem(root, context, runtime.Logger), new LibraryParams.Builder().SetExtras(extras)!.Build());
            },
            error => Fail(error, "root"));

    public IListenableFuture? OnGetChildren(MediaLibrarySession? session, MediaSession.ControllerInfo? browser, string? parentId, int page, int pageSize, LibraryParams? @params)
    {
        if (string.IsNullOrEmpty(parentId) || page < 0 || pageSize <= 0)
        {
            return CarMediaFutures.Done(LibraryResult.OfError(SessionError.ErrorBadValue));
        }

        return CarMediaFutures.From(
            runtime.Library.GetChildrenAsync(parentId, page, pageSize, Cancellation),
            items => LibraryResult.OfItemList(Items(items), @params),
            error => Fail(error, parentId));
    }

    public IListenableFuture? OnGetItem(MediaLibrarySession? session, MediaSession.ControllerInfo? browser, string? mediaId)
    {
        if (string.IsNullOrEmpty(mediaId))
        {
            return CarMediaFutures.Done(LibraryResult.OfError(SessionError.ErrorBadValue));
        }

        return CarMediaFutures.From(
            runtime.Library.GetItemAsync(mediaId, Cancellation),
            item => item is null
                ? LibraryResult.OfError(SessionError.ErrorBadValue)
                : LibraryResult.OfItem(CarMediaItems.ToMediaItem(item, context, runtime.Logger), null),
            error => Fail(error, mediaId));
    }

    // Media3: OnSearch confirms at once, and the number of results arrives through NotifySearchResultChanged
    public IListenableFuture? OnSearch(MediaLibrarySession? session, MediaSession.ControllerInfo? browser, string? query, LibraryParams? @params)
    {
        var text = query ?? string.Empty;
        var key = Key(browser);
        runtime.Library.SearchAsync(text, Cancellation).ContinueWith(
            task =>
            {
                if (task.IsCompletedSuccessfully)
                {
                    _searches.Store(key, text, task.Result);
                    new Handler(Looper.MainLooper!).Post(() => session?.NotifySearchResultChanged(browser!, text, task.Result.Count, @params));
                }
                else
                {
                    // Without a notification, search in Android Auto waits forever; 0 results, and OnGetSearchResult
                    // (with no memory entry) searches again and returns an error (review I7)
                    if (task.Exception?.GetBaseException() is { } error)
                    {
                        Fail(error, "search");
                    }

                    new Handler(Looper.MainLooper!).Post(() => session?.NotifySearchResultChanged(browser!, text, 0, @params));
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.None,
            TaskScheduler.Default);
        return CarMediaFutures.Done(LibraryResult.OfVoid(@params));
    }

    public IListenableFuture? OnGetSearchResult(MediaLibrarySession? session, MediaSession.ControllerInfo? browser, string? query, int page, int pageSize, LibraryParams? @params)
    {
        var text = query ?? string.Empty;
        var key = Key(browser);
        var results = _searches.TryGet(key, text, out var cached)
            ? Task.FromResult(cached)
            : runtime.Library.SearchAsync(text, Cancellation);
        return CarMediaFutures.From(
            results,
            items =>
            {
                _searches.Store(key, text, items);
                return LibraryResult.OfItemList(Items(CarMediaPaging.Slice(items, page, pageSize)), @params);
            },
            error => Fail(error, "search"));
    }

    // The app player plays by MediaId, so items from browsing need no URI (the default callback would reject them).
    // A voice "Play X" (legacy onPlayFromSearch) arrives as an item without MediaId with a query → the first playable
    // SearchAsync result (review I9)
    public IListenableFuture? OnAddMediaItems(MediaSession? mediaSession, MediaSession.ControllerInfo? controller, IList<MediaItem>? mediaItems) =>
        CarMediaFutures.From(ResolveAsync(mediaItems ?? []), items => new JavaList<MediaItem>(items), error => Unsupported(error));

    public IListenableFuture? OnCustomCommand(MediaSession? session, MediaSession.ControllerInfo? controller, SessionCommand? customCommand, Bundle? args)
    {
        if (customCommand?.CustomAction is { } action && CarNowPlayingButtons.Find(runtime.Options.NowPlaying, action) is { } button)
        {
            return CarMediaFutures.From(runtime.Controller.PressAsync(button).ContinueWith(_ => 0, TaskScheduler.Default), _ => new SessionResult(SessionResult.ResultSuccess), _ => new SessionResult(SessionError.ErrorUnknown));
        }

        return CarMediaFutures.Done(new SessionResult(SessionError.ErrorNotSupported));
    }

    public IListenableFuture? OnCustomCommand(MediaSession? session, MediaSession.ControllerInfo? controller, SessionCommand? customCommand, Bundle? args, MediaSession.IProgressReporter? progressReporter) =>
        OnCustomCommand(session, controller, customCommand, args);

    // Default Media3: items as in OnAddMediaItems with the start position unchanged
    public IListenableFuture? OnSetMediaItems(MediaSession? mediaSession, MediaSession.ControllerInfo? controller, IList<MediaItem>? mediaItems, int startIndex, long startPositionMs) =>
        CarMediaFutures.From(
            ResolveAsync(mediaItems ?? []),
            items => new MediaSession.MediaItemsWithStartPosition(items, startIndex, startPositionMs),
            error => Unsupported(error));

    public void OnPostConnect(MediaSession? session, MediaSession.ControllerInfo? controller)
    {
    }

    public void OnDisconnected(MediaSession? session, MediaSession.ControllerInfo? controller)
    {
    }

    public int OnPlayerCommandRequest(MediaSession? session, MediaSession.ControllerInfo? controller, int playerCommand) => SessionResult.ResultSuccess;

    public void OnPlayerInteractionFinished(MediaSession? session, MediaSession.ControllerInfo? controllerInfo, AndroidX.Media3.Common.PlayerCommands? playerCommands)
    {
    }

    public bool OnMediaButtonEvent(MediaSession? session, MediaSession.ControllerInfo? controllerInfo, Android.Content.Intent? intent) => false;

    public IListenableFuture? OnSetRating(MediaSession? session, MediaSession.ControllerInfo? controller, Rating? rating) =>
        CarMediaFutures.Done(new SessionResult(SessionError.ErrorNotSupported));

    public IListenableFuture? OnSetRating(MediaSession? session, MediaSession.ControllerInfo? controller, string? mediaId, Rating? rating) =>
        CarMediaFutures.Done(new SessionResult(SessionError.ErrorNotSupported));

    // Resuming playback after a restart (Android 13+): no saved queue, "not supported" as in the Media3 default
    public IListenableFuture? OnPlaybackResumption(MediaSession? mediaSession, MediaSession.ControllerInfo? controller) =>
        CarMediaFutures.Failed(new Java.Lang.UnsupportedOperationException());

    public IListenableFuture? OnPlaybackResumption(MediaSession? mediaSession, MediaSession.ControllerInfo? controller, bool isForPlayback) =>
        CarMediaFutures.Failed(new Java.Lang.UnsupportedOperationException());

    public IListenableFuture? OnSubscribe(MediaLibrarySession? session, MediaSession.ControllerInfo? browser, string? parentId, LibraryParams? @params) =>
        CarMediaFutures.Done(LibraryResult.OfVoid(@params));

    public IListenableFuture? OnUnsubscribe(MediaLibrarySession? session, MediaSession.ControllerInfo? browser, string? parentId) =>
        CarMediaFutures.Done(LibraryResult.OfVoid());

    // Session buttons (Android Auto shows them as playback actions) with Media3 icons according to state. A legacy
    // action without an icon ends with IllegalArgumentException in Media3 when a controller connects (review C1), so
    // a button without an icon (More: CarPlay only; Custom without an app drawable) is skipped, with a warning once
    // per button
    public List<CommandButton> Buttons(CarPlaybackState state) =>
        [.. CarNowPlayingButtons.Resolve(runtime.Options.NowPlaying, state).Select(Button).OfType<CommandButton>()];

    private CommandButton? Button(CarResolvedNowPlayingButton resolved)
    {
        var icon = resolved.Icon switch
        {
            CarMediaButtonIcon.ShuffleOn => CommandButton.IconShuffleOn,
            CarMediaButtonIcon.ShuffleOff => CommandButton.IconShuffleOff,
            CarMediaButtonIcon.RepeatOne => CommandButton.IconRepeatOne,
            CarMediaButtonIcon.RepeatAll => CommandButton.IconRepeatAll,
            CarMediaButtonIcon.RepeatOff => CommandButton.IconRepeatOff,
            CarMediaButtonIcon.PlaybackRate => CommandButton.IconPlaybackSpeed,
            CarMediaButtonIcon.AddToLibrary => CommandButton.IconPlaylistAdd,
            _ => CommandButton.IconUndefined,
        };
        var builder = new CommandButton.Builder(icon);
        if (icon == CommandButton.IconUndefined)
        {
            if (resolved.Icon != CarMediaButtonIcon.Custom || CustomIcon(resolved.Button.Image) is not (var resource and > 0))
            {
                if (_skippedButtons.Add(resolved.Button.Id))
                {
                    CarMediaLog.Warning(
                        runtime.Logger,
                        $"Now Playing button '{resolved.Button.Id}' ({resolved.Button.Kind}) has no Android icon and is not shown in Android Auto; give a Custom button a drawable (CarImage.FromSystemSymbol(…, androidIconName) or a MauiImage file)",
                        null);
                }

                return null;
            }

            builder.SetCustomIconResId(resource);
        }

        return builder
            .SetDisplayName(resolved.Button.Kind switch
            {
                CarNowPlayingButtonKind.Shuffle => "Shuffle",
                CarNowPlayingButtonKind.Repeat => "Repeat",
                CarNowPlayingButtonKind.PlaybackRate => "Playback speed",
                _ => resolved.Button.Id,
            })!
            .SetSessionCommand(new SessionCommand(resolved.CommandId, Bundle.Empty!))!
            .SetEnabled(resolved.IsEnabled)!
            .Build()!;
    }

    private int CustomIcon(CarImage? image) => image switch
    {
        CarSymbolImage { AndroidIconName: { } name } => context.Resources?.GetIdentifier(name, "drawable", context.PackageName) ?? 0,
        CarFileImage file => context.Resources?.GetIdentifier(Renderers.CarImageNames.AndroidResourceName(file.Path), "drawable", context.PackageName) ?? 0,
        _ => 0,
    };

    private JavaList<MediaItem> Items(IReadOnlyList<CarMediaItem> items) =>
        [.. items.Select(item => CarMediaItems.ToMediaItem(item, context, runtime.Logger))];

    private static string Key(MediaSession.ControllerInfo? browser) => browser is null ? string.Empty : browser.PackageName + "/" + browser.Uid;

    // Cancellation on service shutdown (OnDestroy) is not a library error: no report to ICarErrorHandler
    private LibraryResult Fail(Exception error, string parentId)
    {
        if (error is not System.OperationCanceledException)
        {
            CarMediaLog.LibraryFailed(runtime.Logger, parentId, error);
            runtime.Errors.HandleError(error, new CarErrorContext("Media.Library", null));
        }

        return LibraryResult.OfError(SessionError.ErrorUnknown)!;
    }

    private Java.Lang.Object? Unsupported(Exception error)
    {
        Fail(error, "search");
        return null;
    }

    // Items with a MediaId unchanged; an item with only a query (voice) → the first playable search result
    private async Task<IList<MediaItem>> ResolveAsync(IList<MediaItem> items)
    {
        var resolved = new List<MediaItem>(items.Count);
        foreach (var item in items)
        {
            if (string.IsNullOrEmpty(item.MediaId) && CarMediaRequests.SearchQuery(item) is { Length: > 0 } query
                && (await runtime.Library.SearchAsync(query, Cancellation).ConfigureAwait(false)).FirstOrDefault(i => i.IsPlayable) is { } found)
            {
                resolved.Add(CarMediaItems.ToMediaItem(found, context, runtime.Logger));
            }
            else
            {
                resolved.Add(item);
            }
        }

        return resolved;
    }
}
