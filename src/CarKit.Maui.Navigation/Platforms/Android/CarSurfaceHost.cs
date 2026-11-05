using System.Diagnostics;
using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Hardware.Display;
using Android.OS;
using Android.Views;
using AndroidX.Car.App;
using Microsoft.Maui;
using Microsoft.Maui.Graphics.Platform;
using Microsoft.Maui.Platform;
using AndroidView = Android.Views.View;
using Rect = Android.Graphics.Rect;
using Window = Android.Views.Window;
using SizeF = Microsoft.Maui.Graphics.SizeF;

namespace CarKit.Maui.Navigation;

// Car App Library SurfaceCallback calls (spec: "Android hosting"): all methods are implemented, because default Java
// interface methods in the 1.7.0.1 binding end with AbstractMethodError. The host calls them on the main thread.
internal sealed class CarKitSurfaceCallback(CarSurfaceHost host) : Java.Lang.Object, ISurfaceCallback
{
    public void OnSurfaceAvailable(SurfaceContainer? surfaceContainer)
    {
        if (surfaceContainer is not null)
        {
            host.OnSurfaceAvailable(surfaceContainer);
        }
    }

    public void OnSurfaceDestroyed(SurfaceContainer? surfaceContainer) => host.OnSurfaceDestroyed();

    public void OnVisibleAreaChanged(Rect? visibleArea) => host.OnAreaChanged(visibleArea, stable: false);

    public void OnStableAreaChanged(Rect? stableArea) => host.OnAreaChanged(stableArea, stable: true);

    public void OnScroll(float distanceX, float distanceY) => host.OnGesture(CarMapGestureKind.Scroll, distanceX, distanceY, 1);

    public void OnScale(float focusX, float focusY, float scaleFactor) => host.OnGesture(CarMapGestureKind.Scale, focusX, focusY, scaleFactor);

    public void OnFling(float velocityX, float velocityY) => host.OnGesture(CarMapGestureKind.Fling, velocityX, velocityY, 1);

    public void OnClick(float x, float y) => host.OnGesture(CarMapGestureKind.Click, x, y, 1);
}

// Surface content on the Car App Library Surface (spec: "Navigation module decisions (Stage 8)", Surface rendering):
// renderer → Surface.LockHardwareCanvas + Microsoft.Maui.Graphics PlatformCanvas (no VirtualDisplay); native view and
// MAUI view → a private VirtualDisplay + Presentation. Frame times: renderer drawing or FrameMetrics of the
// Presentation window (API 24). Main thread.
internal sealed class CarSurfaceHost : ICarMapSurfaceHost, IDisposable
{
    private readonly CarMapSurface _surface;
    private readonly CarNavigationRuntime _runtime;
    private readonly Context _context;
    private CarContentStyle _style;
    private readonly Handler _main = new(Looper.MainLooper!);
    private SurfaceContainer? _container;
    private PlatformCanvas? _canvas;
    private VirtualDisplay? _display;
    private Presentation? _presentation;
    private FrameMetricsListener? _frameMetrics;
    private bool _drawScheduled;

    public CarSurfaceHost(CarMapSurface surface, CarNavigationRuntime runtime, Context context, CarContentStyle style)
    {
        _surface = surface;
        _runtime = runtime;
        _context = context;
        _style = style;
    }

    // Day/night (the session's onCarConfigurationChanged): the current surface and later attachments
    public void SetContentStyle(CarContentStyle style)
    {
        _style = style;
        if (_container is not null)
        {
            _surface.SetContentStyle(style);
        }
    }

    // Device tests: a view in a Presentation on a virtual display
    internal Presentation? Presentation => _presentation;

    internal VirtualDisplay? Display => _display;

    public void OnSurfaceAvailable(SurfaceContainer container)
    {
        ReleaseContent();
        _container = container;
        var density = container.Dpi > 0 ? container.Dpi / 160f : 1;
        _surface.Attach(this, _style);
        _surface.SetGeometry(new SizeF(container.Width / density, container.Height / density), density);
        _surface.SetAvailable(true);
        CarNavigationLog.SurfaceChanged(_runtime.Logger, _surface.Kind, "available", _surface.Size.Width, _surface.Size.Height, density);
        BuildContent();
    }

    public void OnSurfaceDestroyed()
    {
        ReleaseContent();
        _container = null;
        _surface.Detach(this);
        CarNavigationLog.SurfaceChanged(_runtime.Logger, _surface.Kind, "destroyed", 0, 0, 0);
    }

    public void Dispose()
    {
        Release();
        _canvas = null;
        _main.Dispose();
    }

    // Disconnect without OnSurfaceDestroyed (end of session)
    public void Release()
    {
        if (_container is not null)
        {
            OnSurfaceDestroyed();
        }
    }

    public void OnAreaChanged(Rect? area, bool stable)
    {
        if (area is null)
        {
            return;
        }

        var rect = CarMapGestureTranslator.Area(area.Left, area.Top, area.Right, area.Bottom, _surface.Density);
        if (stable)
        {
            _surface.SetAreas(null, rect);
        }
        else
        {
            _surface.SetAreas(rect, null);
        }

        Invalidate();
    }

    public void OnGesture(CarMapGestureKind kind, float x, float y, float factor) =>
        _surface.RaiseGesture(CarMapGestureTranslator.Scaled(kind, x, y, factor, _surface.Density));

    public void ContentChanged()
    {
        ReleaseContent();
        BuildContent();
    }

    // Several requests before a frame → one draw on the next turn of the main loop
    public void Invalidate()
    {
        if (_drawScheduled || _container is null || _surface.Content is not CarMapSurfaceContent.Renderer)
        {
            return;
        }

        _drawScheduled = true;
        _main.Post(DrawFrame);
    }

    // One renderer frame on the surface's hardware canvas, in dp units
    internal void DrawFrame()
    {
        _drawScheduled = false;
        if (_container?.Surface is not { IsValid: true } target || _surface.Content is not CarMapSurfaceContent.Renderer renderer)
        {
            return;
        }

        var watch = Stopwatch.StartNew();
        Canvas? native = null;
        try
        {
            native = target.LockHardwareCanvas();
            native!.DrawColor(Android.Graphics.Color.Transparent, PorterDuff.Mode.Clear!);
            _canvas ??= new PlatformCanvas(_context);
            _canvas.Canvas = native;
            _canvas.SaveState();
            _canvas.Scale(_surface.Density, _surface.Density);
            _surface.Draw(renderer.Value, _canvas);
            _canvas.RestoreState();
        }
        catch (Exception ex) when (ex is Java.Lang.IllegalStateException or Java.Lang.IllegalArgumentException)
        {
            // The Surface was destroyed between scheduling and drawing
            CarNavigationLog.Trace(_runtime.Logger, "surface not drawable: " + ex.Message);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Main loop callback: an exception would close the app (review M9)
            _runtime.Errors.HandleError(ex, new CarErrorContext("Navigation.Draw", null));
        }
        finally
        {
            if (native is not null)
            {
                Post(target, native);
                _surface.RecordFrame(watch.Elapsed);
            }
        }
    }

    private void Post(Surface target, Canvas native)
    {
        try
        {
            target.UnlockCanvasAndPost(native);
        }
        catch (Exception ex) when (ex is Java.Lang.IllegalStateException or Java.Lang.IllegalArgumentException)
        {
            CarNavigationLog.Trace(_runtime.Logger, "surface released before the frame was posted: " + ex.Message);
        }
    }

    private void BuildContent()
    {
        if (_container is not { } container)
        {
            return;
        }

        try
        {
            switch (_surface.Content)
            {
                case CarMapSurfaceContent.Renderer:
                    Invalidate();
                    break;
                case CarMapSurfaceContent.Native { Factory: Func<Context, AndroidView> factory }:
                    Present(container, context => factory(context));
                    break;
                case CarMapSurfaceContent.MauiView maui:
                    Present(container, context => maui.Factory().ToPlatform(new MauiContext(_runtime.Services, context)));
                    break;
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            CarNavigationLog.ContentFailed(_runtime.Logger, _surface.Kind, ex);
            _runtime.Errors.HandleError(ex, new CarErrorContext("Navigation.SurfaceContent", null));
        }
    }

    // A private virtual display (OwnContentOnly) on the host Surface and a Presentation with the app view (a
    // TYPE_PRIVATE_PRESENTATION window without a token); size and DPI from SurfaceContainer
    private void Present(SurfaceContainer container, Func<Context, AndroidView> view)
    {
        var displays = (DisplayManager)_context.GetSystemService(Context.DisplayService)!;
        _display = displays.CreateVirtualDisplay(
            "CarKit." + _surface.Kind, container.Width, container.Height, container.Dpi, container.Surface, VirtualDisplayFlags.OwnContentOnly)!;
        _presentation = new Presentation(_context, _display.Display!);
        _presentation.SetContentView(view(_presentation.Context));
        if (OperatingSystem.IsAndroidVersionAtLeast(24) && _presentation.Window is { } window)
        {
            _frameMetrics = new FrameMetricsListener(_surface);
            window.AddOnFrameMetricsAvailableListener(_frameMetrics, _main);
        }

        _presentation.Show();
    }

    private void ReleaseContent()
    {
        _drawScheduled = false;
        if (_presentation is { } presentation)
        {
            if (_frameMetrics is { } metrics && OperatingSystem.IsAndroidVersionAtLeast(24))
            {
                presentation.Window?.RemoveOnFrameMetricsAvailableListener(metrics);
            }

            presentation.Dismiss();
            presentation.Dispose();
        }

        _presentation = null;
        _frameMetrics?.Dispose();
        _frameMetrics = null;
        _display?.Release();
        _display?.Dispose();
        _display = null;
    }

    // FrameMetrics.TOTAL_DURATION of each Presentation window frame (from entry to buffer submission)
    private sealed class FrameMetricsListener(CarMapSurface surface) : Java.Lang.Object, Window.IOnFrameMetricsAvailableListener
    {
        public void OnFrameMetricsAvailable(Window? window, FrameMetrics? frameMetrics, int dropCountSinceLastInvocation)
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(24) && frameMetrics is not null)
            {
                surface.RecordFrame(TimeSpan.FromTicks(frameMetrics.GetMetric((int)FrameMetricsId.TotalDuration) / 100));
            }
        }
    }
}
