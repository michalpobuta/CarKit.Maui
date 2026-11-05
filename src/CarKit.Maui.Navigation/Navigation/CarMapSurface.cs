using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Graphics;

namespace CarKit.Maui.Navigation;

// Surface content set by the app; the platform host builds a view from it for every window/surface
internal abstract record CarMapSurfaceContent
{
    private protected CarMapSurfaceContent()
    {
    }

    public sealed record Renderer(ICarSurfaceRenderer Value) : CarMapSurfaceContent;

    public sealed record MauiView(Func<Microsoft.Maui.Controls.View> Factory) : CarMapSurfaceContent;

    // Native platform view (iOS: Func<UIViewController>, Android: Func<Context, View>)
    public sealed record Native(Delegate Factory) : CarMapSurfaceContent;

    // For the frame log
    public string Mode => this switch
    {
        Renderer => "custom drawing",
        MauiView => "MAUI view",
        _ => "native view",
    };
}

// Platform host (CarPlay window, Android Auto Surface): draws the content and reports geometry, gestures and frames.
// Main thread.
internal interface ICarMapSurfaceHost
{
    // The content changed: a new view or renderer
    void ContentChanged();

    // A new renderer frame (merged into a single frame)
    void Invalidate();
}

// Map surface of one display (spec: "Navigation module decisions (Stage 8)", Map surfaces). It outlives the
// connection: the app content stays, the host attaches on connect and detaches on disconnect. State and events on the
// main thread; public methods from any thread (content changes and Invalidate reach the host through the dispatcher).
internal sealed partial class CarMapSurface : ICarMapSurface
{
    private readonly ICarDispatcher _dispatcher;
    private readonly ICarErrorHandler _errors;
    private readonly ILogger _logger;
    private readonly bool _logFrames;
    private readonly Lock _lock = new();
    private readonly CarFrameStatisticsRecorder _frames = new();
    private CarMapSurfaceContent? _content;
    private ICarMapSurfaceHost? _host;
    private int _framesSinceLog;

    public CarMapSurface(CarMapSurfaceKind kind, ICarDispatcher dispatcher, ICarErrorHandler errors, ILogger logger, bool logFrames = false)
    {
        Kind = kind;
        _dispatcher = dispatcher;
        _errors = errors;
        _logger = logger;
        _logFrames = logFrames;
    }

    public event EventHandler? AvailabilityChanged;

    public event EventHandler? AreasChanged;

    public event EventHandler? PanModeChanged;

    public event EventHandler<CarMapGestureEventArgs>? Gesture;

    public CarMapSurfaceKind Kind { get; }

    public bool IsAvailable { get; private set; }

    public SizeF Size { get; private set; }

    public float Density { get; private set; } = 1;

    public RectF VisibleArea { get; private set; }

    public RectF StableArea { get; private set; }

    public CarContentStyle ContentStyle { get; private set; }

    public bool IsPanning { get; private set; }

    public CarMapFrameStatistics FrameStatistics
    {
        get
        {
            lock (_lock)
            {
                return _frames.Statistics();
            }
        }
    }

    public CarMapSurfaceContent? Content
    {
        get
        {
            lock (_lock)
            {
                return _content;
            }
        }
    }

    // Whether the app set content (Stage 8: POI with the app map on Android Auto only then)
    public bool HasContent => Content is not null;

    public void UseRenderer(ICarSurfaceRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        SetContent(new CarMapSurfaceContent.Renderer(renderer));
    }

    [Experimental("CARKIT0002")]
    public void UseMauiView(Func<Microsoft.Maui.Controls.View> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        SetContent(new CarMapSurfaceContent.MauiView(factory));
    }

    public void ClearContent() => SetContent(null);

    public void Invalidate()
    {
        if (CurrentHost() is { } host)
        {
            Post(host.Invalidate, "Invalidate");
        }
    }

    public void ResetFrameStatistics()
    {
        lock (_lock)
        {
            _frames.Clear();
            _framesSinceLog = 0;
        }
    }

    // From the platform host (main thread)

    public void Attach(ICarMapSurfaceHost host, CarContentStyle style)
    {
        ArgumentNullException.ThrowIfNull(host);
        lock (_lock)
        {
            _host = host;
        }

        ContentStyle = style;
        ResetFrameStatistics();
    }

    public void Detach(ICarMapSurfaceHost host)
    {
        lock (_lock)
        {
            if (!ReferenceEquals(_host, host))
            {
                return;
            }

            _host = null;
        }

        IsPanning = false;
        SetAvailable(false);
    }

    public void SetAvailable(bool available)
    {
        if (IsAvailable == available)
        {
            return;
        }

        IsAvailable = available;
        Raise(AvailabilityChanged, "AvailabilityChanged");
    }

    public void SetGeometry(SizeF size, float density)
    {
        if (Size == size && Density == density)
        {
            return;
        }

        Size = size;
        Density = density > 0 ? density : 1;
        // Without areas from the platform the whole surface is visible and stable
        if (VisibleArea.IsEmpty)
        {
            VisibleArea = new RectF(PointF.Zero, size);
        }

        if (StableArea.IsEmpty)
        {
            StableArea = new RectF(PointF.Zero, size);
        }

        Raise(AreasChanged, "AreasChanged");
    }

    public void SetAreas(RectF? visible, RectF? stable)
    {
        var newVisible = visible ?? VisibleArea;
        var newStable = stable ?? StableArea;
        if (newVisible == VisibleArea && newStable == StableArea)
        {
            return;
        }

        VisibleArea = newVisible;
        StableArea = newStable;
        Raise(AreasChanged, "AreasChanged");
    }

    // Day/night after connecting: the renderer reads ContentStyle in Draw, hence a new frame
    public void SetContentStyle(CarContentStyle style)
    {
        if (ContentStyle == style)
        {
            return;
        }

        ContentStyle = style;
        Invalidate();
    }

    public void SetPanning(bool panning)
    {
        if (IsPanning == panning)
        {
            return;
        }

        IsPanning = panning;
        Raise(PanModeChanged, "PanModeChanged");
    }

    public void RaiseGesture(CarMapGestureEventArgs gesture)
    {
        try
        {
            Gesture?.Invoke(this, gesture);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _errors.HandleError(ex, new CarErrorContext("Navigation.Gesture", null));
        }
    }

    // Frame time of the current content mode; with nav.LogFrameTimes a summary every 120 frames
    public void RecordFrame(TimeSpan frameTime)
    {
        CarMapFrameStatistics? toLog = null;
        lock (_lock)
        {
            _frames.Record(frameTime);
            if (_logFrames && ++_framesSinceLog >= CarFrameStatisticsRecorder.LogInterval)
            {
                _framesSinceLog = 0;
                toLog = _frames.Statistics();
            }
        }

        if (toLog is { } statistics)
        {
            CarNavigationLog.FrameTimes(
                _logger, Kind, Content?.Mode ?? "none", statistics.FrameCount, statistics.Average.TotalMilliseconds, statistics.Median.TotalMilliseconds,
                statistics.Percentile95.TotalMilliseconds, statistics.Maximum.TotalMilliseconds, statistics.SlowFrames, statistics.VerySlowFrames);
        }
    }

    // Drawing by the app renderer: an exception goes to ICarErrorHandler, a frame without content
    public void Draw(ICarSurfaceRenderer renderer, ICanvas canvas)
    {
        try
        {
            renderer.Draw(canvas, VisibleArea, StableArea);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _errors.HandleError(ex, new CarErrorContext("Navigation.Draw", null));
        }
    }

    private void SetContent(CarMapSurfaceContent? content)
    {
        lock (_lock)
        {
            _content = content;
            _frames.Clear();
        }

        if (CurrentHost() is { } host)
        {
            Post(host.ContentChanged, "ContentChanged");
        }
    }

    private ICarMapSurfaceHost? CurrentHost()
    {
        lock (_lock)
        {
            return _host;
        }
    }

    private void Post(Action work, string operation) =>
        _dispatcher.DispatchAsync(() =>
        {
            try
            {
                work();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _errors.HandleError(ex, new CarErrorContext("Navigation." + operation, null));
            }

            return Task.CompletedTask;
        }).Forget(_errors, "Navigation." + operation);

    private void Raise(EventHandler? handler, string operation)
    {
        try
        {
            handler?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _errors.HandleError(ex, new CarErrorContext("Navigation." + operation, null));
        }
    }
}

// Recent frames (max 600) and their statistics; not thread-safe (CarMapSurface holds the lock)
internal sealed class CarFrameStatisticsRecorder
{
    public const int Capacity = 600;
    public const int LogInterval = 120;
    private static readonly TimeSpan _slow = TimeSpan.FromMilliseconds(1000.0 / 60);
    private static readonly TimeSpan _verySlow = TimeSpan.FromMilliseconds(2000.0 / 60);
    private readonly Queue<TimeSpan> _frames = new();

    public void Record(TimeSpan frameTime)
    {
        if (frameTime < TimeSpan.Zero)
        {
            return;
        }

        if (_frames.Count == Capacity)
        {
            _frames.Dequeue();
        }

        _frames.Enqueue(frameTime);
    }

    public void Clear() => _frames.Clear();

    public CarMapFrameStatistics Statistics()
    {
        if (_frames.Count == 0)
        {
            return CarMapFrameStatistics.Empty;
        }

        var sorted = _frames.Order().ToArray();
        return new CarMapFrameStatistics(
            sorted.Length,
            TimeSpan.FromTicks((long)sorted.Average(frame => frame.Ticks)),
            Percentile(sorted, 0.5),
            Percentile(sorted, 0.95),
            sorted[^1],
            sorted.Count(frame => frame > _slow),
            sorted.Count(frame => frame > _verySlow));
    }

    // Nearest-rank method
    private static TimeSpan Percentile(TimeSpan[] sorted, double percentile) =>
        sorted[Math.Clamp((int)Math.Ceiling(percentile * sorted.Length) - 1, 0, sorted.Length - 1)];
}
