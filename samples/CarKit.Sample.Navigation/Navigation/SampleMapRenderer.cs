using CarKit.Maui;
using CarKit.Maui.Navigation;
using Microsoft.Maui.Graphics;

namespace CarKit.Sample.Navigation;

// Sample map state: zoom and pan from map gestures and buttons (main thread)
public sealed class SampleMapState
{
    public float Zoom { get; private set; } = 1;

    public PointF Offset { get; private set; }

    public event EventHandler? Changed;

    public void ZoomBy(float factor)
    {
        Zoom = Math.Clamp(Zoom * factor, 0.5f, 4f);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Pan(PointF delta)
    {
        Offset = new PointF(Offset.X - delta.X, Offset.Y - delta.Y);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Center()
    {
        Offset = PointF.Zero;
        Zoom = 1;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // Car surface gestures (CarPlay, Android Auto, cluster) pan and zoom the same map
    public void Track(ICarMapSurface surface)
    {
        surface.Gesture += (_, e) =>
        {
            switch (e.Kind)
            {
                case CarMapGestureKind.Scroll:
                    Pan(e.Delta);
                    break;
                case CarMapGestureKind.Scale:
                    ZoomBy(e.ScaleFactor);
                    break;
            }
        };
        Changed += (_, _) => surface.Invalidate();
    }
}

// A map drawn by the app (without a map SDK): a street grid, the route and a car at the simulation progress; the same
// code on CarPlay, Android Auto, Dashboard, cluster and the phone screen (Microsoft.Maui.Graphics)
public sealed class SampleMapRenderer(SampleRouteSimulator simulator, SampleMapState map, Func<CarContentStyle> style) : ICarSurfaceRenderer, IDrawable
{
    private static readonly PointF[] _route = [new(0.15f, 0.85f), new(0.15f, 0.55f), new(0.45f, 0.55f), new(0.55f, 0.35f), new(0.85f, 0.3f), new(0.85f, 0.12f)];

    public void Draw(ICanvas canvas, RectF dirtyRect) => Draw(canvas, dirtyRect, dirtyRect);

    public void Draw(ICanvas canvas, RectF visibleArea, RectF stableArea)
    {
        var dark = style() == CarContentStyle.Dark;
        canvas.FillColor = dark ? Color.FromArgb("#1B1F24") : Color.FromArgb("#EEF1EA");
        canvas.FillRectangle(-1, -1, (visibleArea.Right + visibleArea.Left) * 4, (visibleArea.Bottom + visibleArea.Top) * 4);

        canvas.SaveState();
        var size = Math.Max(stableArea.Width, stableArea.Height) * map.Zoom;
        var origin = new PointF(stableArea.Center.X - (size / 2) + map.Offset.X, stableArea.Center.Y - (size / 2) + map.Offset.Y);

        // Streets
        canvas.StrokeColor = dark ? Color.FromArgb("#2E353D") : Colors.White;
        canvas.StrokeSize = 6;
        for (var i = 0; i <= 10; i++)
        {
            var t = i / 10f;
            canvas.DrawLine(origin.X + (t * size), origin.Y, origin.X + (t * size), origin.Y + size);
            canvas.DrawLine(origin.X, origin.Y + (t * size), origin.X + size, origin.Y + (t * size));
        }

        // The route and the car (simulation progress)
        var path = new PathF();
        path.MoveTo(Point(origin, size, _route[0]));
        foreach (var point in _route.Skip(1))
        {
            path.LineTo(Point(origin, size, point));
        }

        canvas.StrokeColor = Color.FromArgb("#1E88E5");
        canvas.StrokeSize = 10;
        canvas.StrokeLineCap = LineCap.Round;
        canvas.StrokeLineJoin = LineJoin.Round;
        canvas.DrawPath(path);
        var car = Point(origin, size, Along(simulator.RouteProgress));
        canvas.FillColor = Color.FromArgb("#E53935");
        canvas.FillCircle(car.X, car.Y, 12);
        canvas.StrokeColor = Colors.White;
        canvas.StrokeSize = 3;
        canvas.DrawCircle(car.X, car.Y, 12);
        canvas.RestoreState();

        // The trip destination in the stable area (always visible)
        if (simulator.Destination is { } destination)
        {
            canvas.FontColor = dark ? Colors.White : Colors.Black;
            canvas.FontSize = 18;
            canvas.DrawString(destination.Name, stableArea.X + 16, stableArea.Y + 16, stableArea.Width - 32, 30, HorizontalAlignment.Left, VerticalAlignment.Top);
        }
    }

    private static PointF Point(PointF origin, float size, PointF relative) => new(origin.X + (relative.X * size), origin.Y + (relative.Y * size));

    // A point on the route polyline for progress 0–1
    private static PointF Along(double progress)
    {
        var segments = _route.Length - 1;
        var position = Math.Clamp(progress, 0, 1) * segments;
        var index = Math.Min((int)position, segments - 1);
        var t = (float)(position - index);
        var (a, b) = (_route[index], _route[index + 1]);
        return new PointF(a.X + ((b.X - a.X) * t), a.Y + ((b.Y - a.Y) * t));
    }
}
