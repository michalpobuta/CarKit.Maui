using System.Diagnostics;
using CarPlay;
using CoreAnimation;
using Foundation;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Graphics.Platform;
using Microsoft.Maui.Platform;
using UIKit;

namespace CarKit.Maui.Navigation;

// Surface content in a CarPlay window (CPWindow, Dashboard window, cluster window): a container with the renderer view
// (PlatformGraphicsView), an app controller or a MAUI view; geometry and areas from the container layout, frame times
// (renderer drawing or CADisplayLink intervals for native views). Main thread.
internal sealed class CarSurfaceWindowHost : ICarMapSurfaceHost, IDisposable
{
    private readonly UIWindow _window;
    private readonly CarMapSurface _surface;
    private readonly CarNavigationRuntime _runtime;
    private readonly CarContentStyle _style;
    private readonly bool _followTraits;
    private SurfaceContainerController? _container;
    private PlatformGraphicsView? _graphics;
    private CADisplayLink? _displayLink;
    private double _lastTimestamp;
    private bool _attached;

    // followTraits: style from the window traits (Dashboard); CarScreen and the cluster get changes from the scene
    // delegates
    public CarSurfaceWindowHost(UIWindow window, CarMapSurface surface, CarNavigationRuntime runtime, CarContentStyle style, bool followTraits = false)
    {
        _window = window;
        _surface = surface;
        _runtime = runtime;
        _style = style;
        _followTraits = followTraits;
    }

    public static CarContentStyle StyleOf(UITraitCollection traits) =>
        traits.UserInterfaceStyle == UIUserInterfaceStyle.Dark ? CarContentStyle.Dark : CarContentStyle.Light;

    // Device tests: a container in a substitute window
    internal UIViewController? Container => _container;

    public void Attach()
    {
        _attached = true;
        _surface.Attach(this, _style);
        ContentChanged();
        _surface.SetAvailable(true);
        CarNavigationLog.SurfaceChanged(_runtime.Logger, _surface.Kind, "available", _surface.Size.Width, _surface.Size.Height, _surface.Density);
    }

    public void Detach()
    {
        if (!_attached)
        {
            return;
        }

        _attached = false;
        StopDisplayLink();
        _graphics?.Dispose();
        _graphics = null;
        _container?.Dispose();
        _container = null;
        _window.RootViewController = null;
        _surface.Detach(this);
        CarNavigationLog.SurfaceChanged(_runtime.Logger, _surface.Kind, "gone", 0, 0, 0);
    }

    public void Dispose() => Detach();

    public void ContentChanged()
    {
        if (!_attached)
        {
            return;
        }

        StopDisplayLink();
        _graphics = null;
        _container = new SurfaceContainerController(this);
        try
        {
            switch (_surface.Content)
            {
                case CarMapSurfaceContent.Renderer:
                    _graphics = new PlatformGraphicsView(new SurfaceDrawable(this), new DirectRenderer())
                    {
                        AutoresizingMask = UIViewAutoresizing.FlexibleDimensions,
                        BackgroundColor = UIColor.Clear,
                    };
                    _container.Host(_graphics);
                    break;
                case CarMapSurfaceContent.Native { Factory: Func<UIViewController> factory }:
                    _container.Host(factory());
                    StartDisplayLink();
                    break;
                case CarMapSurfaceContent.MauiView maui:
                    _container.Host(maui.Factory().ToPlatform(new MauiContext(_runtime.Services)));
                    StartDisplayLink();
                    break;
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            CarNavigationLog.ContentFailed(_runtime.Logger, _surface.Kind, ex);
            _runtime.Errors.HandleError(ex, new CarErrorContext("Navigation.SurfaceContent", null));
        }

        _window.RootViewController = _container;
        UpdateGeometry();
    }

    public void Invalidate() => _graphics?.SetNeedsDisplay();

    // From the container layout: size, car screen density, the visible (safe) area and the stable one (CarPlay map
    // buttons)
    internal void UpdateGeometry()
    {
        if (!_attached || _container?.View is not { } view)
        {
            return;
        }

        var bounds = view.Bounds;
        var scale = (float)(_window.TraitCollection.DisplayScale > 0 ? _window.TraitCollection.DisplayScale : 2);
        var before = (_surface.Size, _surface.VisibleArea, _surface.StableArea);
        _surface.SetGeometry(new SizeF((float)bounds.Width, (float)bounds.Height), scale);
        var safe = view.SafeAreaLayoutGuide.LayoutFrame;
        var visible = new RectF((float)safe.X, (float)safe.Y, (float)safe.Width, (float)safe.Height);
        var stable = visible;
        if (_window is CPWindow carWindow && carWindow.MapButtonSafeAreaLayoutGuide is { } buttons)
        {
            var frame = view.ConvertRectFromView(buttons.LayoutFrame, carWindow);
            stable = new RectF((float)frame.X, (float)frame.Y, (float)frame.Width, (float)frame.Height);
        }

        _surface.SetAreas(visible, stable);
        if (before != (_surface.Size, _surface.VisibleArea, _surface.StableArea))
        {
            Invalidate();
        }
    }

    internal void TraitsChanged(UITraitCollection traits)
    {
        if (_attached && _followTraits)
        {
            _surface.SetContentStyle(StyleOf(traits));
        }
    }

    internal void Draw(ICanvas canvas)
    {
        if (_surface.Content is CarMapSurfaceContent.Renderer renderer)
        {
            var watch = Stopwatch.StartNew();
            _surface.Draw(renderer.Value, canvas);
            _surface.RecordFrame(watch.Elapsed);
        }
    }

    // Native and MAUI views: the interval between screen frames (a main thread stall lengthens the interval)
    private void StartDisplayLink()
    {
        _lastTimestamp = 0;
        _displayLink = CADisplayLink.Create(() =>
        {
            if (_displayLink is not { } link)
            {
                return;
            }

            if (_lastTimestamp > 0)
            {
                _surface.RecordFrame(TimeSpan.FromSeconds(link.Timestamp - _lastTimestamp));
            }

            _lastTimestamp = link.Timestamp;
        });
        _displayLink.AddToRunLoop(NSRunLoop.Main, NSRunLoopMode.Common);
    }

    private void StopDisplayLink()
    {
        _displayLink?.Invalidate();
        _displayLink = null;
    }

    private sealed class SurfaceDrawable(CarSurfaceWindowHost host) : IDrawable
    {
        public void Draw(ICanvas canvas, RectF dirtyRect) => host.Draw(canvas);
    }

    // Content container: observes the layout and the safe area of the car window
    private sealed class SurfaceContainerController(CarSurfaceWindowHost host) : UIViewController
    {
        public void Host(UIView content)
        {
            content.Frame = View!.Bounds;
            content.AutoresizingMask = UIViewAutoresizing.FlexibleDimensions;
            View.AddSubview(content);
        }

        public void Host(UIViewController content)
        {
            AddChildViewController(content);
            Host(content.View!);
            content.DidMoveToParentViewController(this);
        }

        public override void ViewDidLayoutSubviews()
        {
            base.ViewDidLayoutSubviews();
            host.UpdateGeometry();
        }

        public override void ViewSafeAreaInsetsDidChange()
        {
            base.ViewSafeAreaInsetsDidChange();
            host.UpdateGeometry();
        }

        // iOS 17+ recommends registerForTraitChanges, but UIKit still calls traitCollectionDidChange; the library
        // minimum is iOS 15
        public override void TraitCollectionDidChange(UITraitCollection? previousTraitCollection)
        {
#pragma warning disable CA1422 // obsolete since iOS 17, the only trait change API for iOS 15–16
            base.TraitCollectionDidChange(previousTraitCollection);
#pragma warning restore CA1422
            host.TraitsChanged(TraitCollection);
        }
    }
}
