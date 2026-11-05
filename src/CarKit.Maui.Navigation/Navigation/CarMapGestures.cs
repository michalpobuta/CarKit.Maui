using Microsoft.Maui.Graphics;

namespace CarKit.Maui.Navigation;

// Direction of the CarPlay pan panel button (CPPanDirection without the platform type)
internal enum CarPanDirection
{
    Left,
    Right,
    Up,
    Down,
}

// CarPlay gestures (CPMapTemplate delegate) in the Android Auto convention (SurfaceCallback): translation and scale are
// cumulative from the start of the gesture, and surface events carry the increment since the previous event (spec:
// "Navigation module decisions (Stage 8)", Gestures). Main thread only.
internal sealed class CarMapGestureTranslator
{
    private PointF _lastTranslation;
    private float _lastScale = 1;

    public void PanBegan() => _lastTranslation = PointF.Zero;

    // Finger to the left (negative translation) = positive X distance, like GestureDetector.onScroll
    public CarMapGestureEventArgs? PanUpdated(PointF translation)
    {
        var delta = new PointF(_lastTranslation.X - translation.X, _lastTranslation.Y - translation.Y);
        _lastTranslation = translation;
        return delta == PointF.Zero ? null : new CarMapGestureEventArgs(CarMapGestureKind.Scroll, PointF.Zero, delta, PointF.Zero, 1);
    }

    public static CarMapGestureEventArgs PanEnded(PointF velocity) =>
        new(CarMapGestureKind.Fling, PointF.Zero, PointF.Zero, velocity, 1);

    public void ZoomBegan() => _lastScale = 1;

    public CarMapGestureEventArgs? ZoomUpdated(PointF center, float scale)
    {
        if (scale <= 0 || _lastScale <= 0)
        {
            return null;
        }

        var factor = scale / _lastScale;
        _lastScale = scale;
        return factor == 1 ? null : new CarMapGestureEventArgs(CarMapGestureKind.Scale, center, PointF.Zero, PointF.Zero, factor);
    }

    // Pan panel buttons (cars with a rotary knob): a step of ¼ of the surface dimension toward the arrow
    public static CarMapGestureEventArgs PanStep(CarPanDirection direction, SizeF size) => direction switch
    {
        CarPanDirection.Left => Scroll(-size.Width / 4, 0),
        CarPanDirection.Right => Scroll(size.Width / 4, 0),
        CarPanDirection.Up => Scroll(0, -size.Height / 4),
        _ => Scroll(0, size.Height / 4),
    };

    // CarPlay cluster: steering wheel zoom buttons → scale ×2 or ×0.5 at the center
    public static CarMapGestureEventArgs Zoom(bool zoomIn, SizeF size) =>
        new(CarMapGestureKind.Scale, new PointF(size.Width / 2, size.Height / 2), PointF.Zero, PointF.Zero, zoomIn ? 2f : 0.5f);

    // Android Auto: pixels → density-independent units
    public static CarMapGestureEventArgs Scaled(CarMapGestureKind kind, float x, float y, float factor, float density)
    {
        var d = density > 0 ? density : 1;
        return kind switch
        {
            CarMapGestureKind.Scroll => new(kind, PointF.Zero, new PointF(x / d, y / d), PointF.Zero, 1),
            CarMapGestureKind.Fling => new(kind, PointF.Zero, PointF.Zero, new PointF(x / d, y / d), 1),
            // Focus -1 = an event without a touch (e.g. the host's zoom button)
            CarMapGestureKind.Scale => new(kind, x < 0 || y < 0 ? new PointF(x, y) : new PointF(x / d, y / d), PointF.Zero, PointF.Zero, factor),
            _ => new(kind, new PointF(x / d, y / d), PointF.Zero, PointF.Zero, 1),
        };
    }

    // Android rectangle (px) → units
    public static RectF Area(int left, int top, int right, int bottom, float density)
    {
        var d = density > 0 ? density : 1;
        return new RectF(left / d, top / d, (right - left) / d, (bottom - top) / d);
    }

    private static CarMapGestureEventArgs Scroll(float x, float y) => new(CarMapGestureKind.Scroll, PointF.Zero, new PointF(x, y), PointF.Zero, 1);
}
