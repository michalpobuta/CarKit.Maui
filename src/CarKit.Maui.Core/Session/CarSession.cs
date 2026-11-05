using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace CarKit.Maui;

// Platform-shared implementation of ICarSession: stack, validation, main thread, events. Operations are serialized by a
// gate; events are emitted after it is released (an event handler may call the session). The stack holds pairs (the
// app's original, the template sent to the host after validation/fallback/truncation).
internal sealed class CarSession : ICarSession, IDisposable
{
    private readonly ICarHost _host;
    private readonly CarKitOptions _options;
    private readonly ICarDispatcher _dispatcher;
    private readonly ICarErrorHandler _errorHandler;
    private readonly ILogger _logger;
    private readonly CarTemplateValidator _validator;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _connection = new();
    private readonly CancellationToken _token;
    private readonly Lock _state = new();
    private readonly List<Entry> _stack = [];
    private Entry? _presented;
    // Modals closed by the host during the current operation (events after it ends)
    private readonly List<CarTemplate> _closedDuringOperation = [];
    private int _connected = 1;

    public CarSession(ICarHost host, CarKitOptions options, ICarDispatcher dispatcher, ICarErrorHandler errorHandler, ILogger logger)
    {
        _host = host;
        _options = options;
        _dispatcher = dispatcher;
        _errorHandler = errorHandler;
        _logger = logger;
        _validator = new CarTemplateValidator(options, host.Capabilities, logger);
        _token = _connection.Token;
        ContentStyle = host.ContentStyle;
    }

    public event EventHandler<CarTemplateEventArgs>? TemplateAppeared;

    public event EventHandler<CarTemplateEventArgs>? TemplateDisappeared;

    public event EventHandler<CarContentStyle>? ContentStyleChanged;

    public event EventHandler? Disconnected;

    public CarPlatform Platform => _host.Platform;

    public ICarCapabilities Capabilities => _host.Capabilities;

    public CarContentStyle ContentStyle { get; private set; }

    public IReadOnlyList<CarTemplate> Stack
    {
        get
        {
            lock (_state)
            {
                return [.. _stack.Select(e => e.Original)];
            }
        }
    }

    public CarTemplate? Presented
    {
        get
        {
            lock (_state)
            {
                return _presented?.Original;
            }
        }
    }

    public bool IsConnected => Volatile.Read(ref _connected) == 1;

    // Stack depth limit from the category rules (Stage 5: auto-browse leaves room for Now Playing and Up Next)
    internal int MaxStackDepth => _validator.MaxDepth;

    public CancellationToken ConnectionToken => _token;

    public TNative? GetNative<TNative>() where TNative : class => _host.GetNative<TNative>();

    public Task SetRootAsync(CarTemplate root, bool animated = false)
    {
        ArgumentNullException.ThrowIfNull(root);
        return RunAsync(async () =>
        {
            ThrowIfModal(root, nameof(SetRootAsync));
            var resolved = _validator.Prepare(root, resultingDepth: 1);
            await NativeAsync("SetRoot", resolved, () => _host.SetRootAsync(resolved, animated)).ConfigureAwait(false);
            CarTemplate? previous;
            lock (_state)
            {
                previous = _stack.Count > 0 ? _stack[^1].Original : null;
                _stack.Clear();
                _stack.Add(new Entry(root, resolved));
            }

            return () =>
            {
                RaiseDisappeared(previous, animated);
                RaiseAppeared(root, animated);
            };
        });
    }

    public Task PushAsync(CarTemplate template, bool animated = true)
    {
        ArgumentNullException.ThrowIfNull(template);
        return RunAsync(async () =>
        {
            ThrowIfModal(template, nameof(PushAsync));
            int depth;
            lock (_state)
            {
                if (_stack.Count == 0)
                {
                    throw new CarStackException("Set a root template before pushing.");
                }

                if (_stack.Exists(e => e.Original.Id == template.Id))
                {
                    throw new CarStackException($"A template with Id '{template.Id}' is already on the stack.");
                }

                depth = _stack.Count + 1;
            }

            var resolved = _validator.Prepare(template, depth);
            await NativeAsync("Push", resolved, () => _host.PushAsync(resolved, animated)).ConfigureAwait(false);
            CarTemplate previous;
            lock (_state)
            {
                previous = _stack[^1].Original;
                _stack.Add(new Entry(template, resolved));
            }

            return () =>
            {
                RaiseDisappeared(previous, animated);
                RaiseAppeared(template, animated);
            };
        });
    }

    public Task PopAsync(bool animated = true) => RunAsync(async () =>
    {
        int index;
        lock (_state)
        {
            if (_stack.Count <= 1)
            {
                throw new CarStackException("The root template cannot be popped.");
            }

            index = _stack.Count - 2;
        }

        await NativeAsync("Pop", null, () => _host.PopAsync(animated)).ConfigureAwait(false);
        var (popped, top) = RemoveAbove(index);
        return () =>
        {
            RaiseDisappeared(popped, animated);
            RaiseAppeared(top, animated);
        };
    });

    public Task PopToAsync(CarTemplate template, bool animated = true)
    {
        ArgumentNullException.ThrowIfNull(template);
        return RunAsync(async () =>
        {
            int index;
            CarTemplate target;
            lock (_state)
            {
                index = _stack.FindIndex(e => e.Original.Id == template.Id);
                if (index < 0)
                {
                    throw new CarStackException($"No template with Id '{template.Id}' is on the stack.");
                }

                if (index == _stack.Count - 1)
                {
                    return null;
                }

                target = _stack[index].Sent;
            }

            await NativeAsync("PopTo", target, () => _host.PopToAsync(target, animated)).ConfigureAwait(false);
            var (popped, top) = RemoveAbove(index);
            return () =>
            {
                RaiseDisappeared(popped, animated);
                RaiseAppeared(top, animated);
            };
        });
    }

    public Task PopToRootAsync(bool animated = true) => RunAsync(async () =>
    {
        lock (_state)
        {
            if (_stack.Count == 0)
            {
                throw new CarStackException("There is no root template.");
            }

            if (_stack.Count == 1)
            {
                return null;
            }
        }

        await NativeAsync("PopToRoot", null, () => _host.PopToRootAsync(animated)).ConfigureAwait(false);
        var (popped, top) = RemoveAbove(0);
        return () =>
        {
            RaiseDisappeared(popped, animated);
            RaiseAppeared(top, animated);
        };
    });

    public Task PresentAsync(CarModalTemplate modal, bool animated = true)
    {
        ArgumentNullException.ThrowIfNull(modal);
        return RunAsync(async () =>
        {
            int screenDepth;
            lock (_state)
            {
                if (_presented is { } current)
                {
                    throw new CarStackException($"{current.Original.Kind} is already presented; dismiss it first.");
                }

                screenDepth = _stack.Count + 1;
            }

            // On Android a modal without a native counterpart becomes a screen and uses up a stack step
            var resolved = _validator.Prepare(modal, resultingDepth: null, depthIfScreen: screenDepth);
            await NativeAsync("Present", resolved, () => _host.PresentAsync(resolved, animated)).ConfigureAwait(false);
            lock (_state)
            {
                _presented = new Entry(modal, resolved);
            }

            return () => RaiseAppeared(modal, animated);
        });
    }

    public Task DismissAsync(bool animated = true) => RunAsync(async () =>
    {
        if (Presented is null)
        {
            return null;
        }

        await NativeAsync("Dismiss", null, () => _host.DismissAsync(animated)).ConfigureAwait(false);
        var dismissed = ClearPresented();
        return () => RaiseDisappeared(dismissed, animated);
    });

    public Task UpdateAsync(CarTemplate updated)
    {
        ArgumentNullException.ThrowIfNull(updated);
        return RunAsync(async () =>
        {
            int index;
            Entry existing;
            int screenDepth;
            lock (_state)
            {
                index = _stack.FindIndex(e => e.Original.Id == updated.Id);
                existing = index >= 0 ? _stack[index]
                    : _presented is { } presented && presented.Original.Id == updated.Id ? presented
                    : throw new CarStackException($"No template with Id '{updated.Id}' is on the stack or presented.");
                screenDepth = _stack.Count + 1;
            }

            if (existing.Original.GetType() != updated.GetType())
            {
                throw new CarStackException($"UpdateAsync cannot change {existing.Original.Kind} into {updated.Kind}; push a new template instead.");
            }

            var resolved = _validator.Prepare(updated, resultingDepth: null, depthIfScreen: index >= 0 ? null : screenDepth);
            await NativeAsync("Update", resolved, () => _host.UpdateAsync(existing.Sent, resolved)).ConfigureAwait(false);
            lock (_state)
            {
                if (index >= 0)
                {
                    _stack[index] = new Entry(updated, resolved);
                }
                else
                {
                    _presented = new Entry(updated, resolved);
                }
            }

            return null;
        });
    }

    // Calls from the host. The host passes the Id of the template that actually disappeared from the platform, because
    // an app operation may have changed the top of the stack in the meantime.

    public Task OnHostPoppedAsync(string templateId) => RunHostAsync(() =>
    {
        CarTemplate popped;
        CarTemplate top;
        bool wasTop;
        lock (_state)
        {
            var index = _stack.FindIndex(e => e.Original.Id == templateId);
            if (index <= 0)
            {
                return null;
            }

            wasTop = index == _stack.Count - 1;
            popped = _stack[index].Original;
            _stack.RemoveAt(index);
            top = _stack[^1].Original;
        }

        return () =>
        {
            RaiseDisappeared(popped, animated: true);
            if (wasTop)
            {
                RaiseAppeared(top, animated: true);
            }
        };
    });

    public Task OnHostDismissedAsync(string templateId) => RunHostAsync(() =>
    {
        CarTemplate dismissed;
        lock (_state)
        {
            if (_presented is not { } presented || presented.Original.Id != templateId)
            {
                return null;
            }

            dismissed = presented.Original;
            _presented = null;
        }

        return () => RaiseDisappeared(dismissed, animated: true);
    });

    // The host closes the presented modal as part of its own operation (Android Auto: a modal screen popped on push or
    // on a new root). Called only from inside an ICarHost call, when this operation holds the gate: without waiting for
    // the gate, TemplateDisappeared after the operation ends. The next app operation already sees Presented == null.
    public void OnHostClosedModalDuringOperation(string templateId)
    {
        lock (_state)
        {
            if (_presented is not { } presented || presented.Original.Id != templateId)
            {
                return;
            }

            _presented = null;
            _closedDuringOperation.Add(presented.Original);
        }
    }

    public Task OnContentStyleChangedAsync(CarContentStyle style) => RunHostAsync(() =>
    {
        ContentStyle = style;
        return () => ContentStyleChanged?.Invoke(this, style);
    });

    public async Task StartAsync(ICarAppHandler handler)
    {
        await _dispatcher.DispatchAsync(async () =>
        {
            try
            {
                await handler.OnConnectedAsync(this, _token).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsCancellation(ex))
            {
                // Disconnect during loading — expected
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                ReportError(ex, "OnConnected", null);
            }
        }).ConfigureAwait(false);
    }

    public async Task StopAsync(ICarAppHandler handler)
    {
        if (Interlocked.Exchange(ref _connected, 0) == 0)
        {
            return;
        }

        try
        {
            await _connection.CancelAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // A throwing app token callback must not interrupt the disconnect
            ReportError(ex, "Disconnect", null);
        }

        await _dispatcher.DispatchAsync(async () =>
        {
            RaiseSafely(() => Disconnected?.Invoke(this, EventArgs.Empty), "Disconnected");
            try
            {
                await handler.OnDisconnectedAsync(this).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                ReportError(ex, "OnDisconnected", null);
            }
        }).ConfigureAwait(false);
    }

    // User callbacks: always on the main thread, exceptions go to ICarErrorHandler

    public CarItemContext CreateContext(CarElement element) => new(this, element, _token);

    public Task InvokeAsync<TContext>(Func<TContext, Task>? handler, TContext context, string operation)
        where TContext : CarItemContext
    {
        if (handler is null)
        {
            return Task.CompletedTask;
        }

        return _dispatcher.DispatchAsync(async () =>
        {
            try
            {
                await handler(context).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsCancellation(ex))
            {
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                ReportError(ex, operation, context.Element);
            }
        });
    }

    public Task<IReadOnlyList<CarListItem>> SearchAsync(CarSearchTemplate template, string text)
    {
        if (template.OnSearchTextChanged is not { } handler)
        {
            return Task.FromResult<IReadOnlyList<CarListItem>>([]);
        }

        return _dispatcher.DispatchAsync<IReadOnlyList<CarListItem>>(async () =>
        {
            try
            {
                return await handler(new CarSearchContext(this, template, text, _token)).ConfigureAwait(false) ?? [];
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                if (!IsCancellation(ex))
                {
                    ReportError(ex, "SearchTextChanged", template);
                }

                return [];
            }
        });
    }

    public void Dispose()
    {
        _gate.Dispose();
        _connection.Dispose();
    }

    private async Task RunAsync(Func<Task<Action?>> operation)
    {
        ThrowIfDisconnected();
        try
        {
            await _gate.WaitAsync(_token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex)
        {
            throw new CarNotConnectedException("The car disconnected while the operation was waiting.", ex);
        }

        Action? raise;
        CarTemplate[] closed;
        try
        {
            ThrowIfDisconnected();
            raise = await _dispatcher.DispatchAsync(operation).ConfigureAwait(false);
        }
        finally
        {
            closed = TakeClosedDuringOperation();
            _gate.Release();
            if (closed.Length > 0)
            {
                // Also when the operation failed: the modal has already disappeared from the platform
                await RaiseAsync(() => Array.ForEach(closed, modal => RaiseDisappeared(modal, animated: true))).ConfigureAwait(false);
            }
        }

        await RaiseAsync(raise).ConfigureAwait(false);
    }

    private CarTemplate[] TakeClosedDuringOperation()
    {
        lock (_state)
        {
            CarTemplate[] closed = [.. _closedDuringOperation];
            _closedDuringOperation.Clear();
            return closed;
        }
    }

    private async Task RunHostAsync(Func<Action?> update)
    {
        if (!IsConnected)
        {
            return;
        }

        try
        {
            await _gate.WaitAsync(_token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        Action? raise;
        try
        {
            raise = update();
        }
        finally
        {
            _gate.Release();
        }

        await RaiseAsync(raise).ConfigureAwait(false);
    }

    private Task RaiseAsync(Action? raise) => raise is null
        ? Task.CompletedTask
        : _dispatcher.DispatchAsync(() =>
        {
            RaiseSafely(raise, "Event");
            return Task.CompletedTask;
        });

    private async Task NativeAsync(string operation, CarTemplate? template, Func<Task> call)
    {
        if (_options.Diagnostics.LogNativeCalls)
        {
            CarKitLog.NativeCall(_logger, operation, template?.Kind, template?.Id);
        }

        try
        {
            await call().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not CarKitException && !IsCancellation(ex) && ex is not OutOfMemoryException)
        {
            throw new CarPlatformException($"{operation} failed on {Platform}: {ex.Message}", ex);
        }
    }

    private (CarTemplate Popped, CarTemplate Top) RemoveAbove(int index)
    {
        lock (_state)
        {
            var popped = _stack[^1].Original;
            _stack.RemoveRange(index + 1, _stack.Count - index - 1);
            return (popped, _stack[^1].Original);
        }
    }

    private CarTemplate ClearPresented()
    {
        lock (_state)
        {
            var dismissed = _presented!.Original;
            _presented = null;
            return dismissed;
        }
    }

    private void RaiseAppeared(CarTemplate template, bool animated) =>
        TemplateAppeared?.Invoke(this, new CarTemplateEventArgs(template, animated));

    private void RaiseDisappeared(CarTemplate? template, bool animated)
    {
        if (template is not null)
        {
            TemplateDisappeared?.Invoke(this, new CarTemplateEventArgs(template, animated));
        }
    }

    [SuppressMessage("Design", "CA1031", Justification = "An exception from a user event handler must not abort the session operation.")]
    private void RaiseSafely(Action raise, string operation)
    {
        try
        {
            raise();
        }
        catch (Exception ex)
        {
            ReportError(ex, operation, null);
        }
    }

    private void ReportError(Exception exception, string operation, CarElement? element) =>
        _errorHandler.HandleError(exception, new CarErrorContext(operation, element));

    // Cancellation after a disconnect (a token or an operation rejected after the disconnect) is not an app error
    private bool IsCancellation(Exception exception) =>
        (exception is OperationCanceledException && _token.IsCancellationRequested)
        || (exception is CarNotConnectedException && !IsConnected);

    private void ThrowIfDisconnected()
    {
        if (!IsConnected)
        {
            throw new CarNotConnectedException("The car is not connected.");
        }
    }

    private static void ThrowIfModal(CarTemplate template, string operation)
    {
        if (template is CarModalTemplate)
        {
            throw new CarStackException($"{template.Kind} is modal; show it with PresentAsync instead of {operation}.");
        }
    }

    private sealed record Entry(CarTemplate Original, CarTemplate Sent);
}
