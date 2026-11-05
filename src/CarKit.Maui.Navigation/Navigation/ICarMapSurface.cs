using System.Diagnostics.CodeAnalysis;
using Microsoft.Maui.Graphics;

namespace CarKit.Maui.Navigation;

/// <summary>A car display that can show the app's map.</summary>
public enum CarMapSurfaceKind
{
    /// <summary>The main car screen: the CarPlay window behind the map template, the Android Auto surface behind the navigation template.</summary>
    CarScreen,

    /// <summary>The CarPlay Dashboard (iOS 13.4; <c>CarKitIosDashboard</c>).</summary>
    Dashboard,

    /// <summary>The instrument cluster: CarPlay (iOS 15.4; <c>CarKitIosInstrumentCluster</c>) or the Android Auto cluster display (Car App API 6; <c>CarKitAndroidCluster</c>).</summary>
    InstrumentCluster,
}

/// <summary>A gesture the driver made on a map surface.</summary>
public enum CarMapGestureKind
{
    /// <summary>A drag; <see cref="CarMapGestureEventArgs.Delta"/> is the distance since the previous scroll.</summary>
    Scroll,

    /// <summary>A pinch or zoom button; <see cref="CarMapGestureEventArgs.ScaleFactor"/> is the change since the previous scale.</summary>
    Scale,

    /// <summary>A fling at the end of a drag; <see cref="CarMapGestureEventArgs.Velocity"/> is in units per second.</summary>
    Fling,

    /// <summary>A tap at <see cref="CarMapGestureEventArgs.Position"/>.</summary>
    Click,
}

/// <summary>Data of <see cref="ICarMapSurface.Gesture"/>, in device-independent units (iOS points, Android dp).</summary>
public sealed class CarMapGestureEventArgs : EventArgs
{
    /// <summary>Creates the event data.</summary>
    /// <param name="kind">The gesture.</param>
    /// <param name="position">The focus of a scale, or the tapped point.</param>
    /// <param name="delta">
    /// The scroll distance since the previous scroll, like Android <c>GestureDetector</c>: moving the finger to the left gives
    /// a positive X, so the map content moves left by that much.
    /// </param>
    /// <param name="velocity">The fling velocity per second, in the direction the finger moved.</param>
    /// <param name="scaleFactor">The scale change since the previous scale; above 1 zooms in.</param>
    public CarMapGestureEventArgs(CarMapGestureKind kind, PointF position, PointF delta, PointF velocity, float scaleFactor)
    {
        Kind = kind;
        Position = position;
        Delta = delta;
        Velocity = velocity;
        ScaleFactor = scaleFactor;
    }

    /// <summary>Gets the gesture.</summary>
    public CarMapGestureKind Kind { get; }

    /// <summary>Gets the focus of a scale or the tapped point.</summary>
    public PointF Position { get; }

    /// <summary>Gets the scroll distance since the previous scroll.</summary>
    public PointF Delta { get; }

    /// <summary>Gets the fling velocity per second.</summary>
    public PointF Velocity { get; }

    /// <summary>Gets the scale change since the previous scale.</summary>
    public float ScaleFactor { get; }
}

/// <summary>How smoothly a map surface renders: frame times of the recent frames (up to 600).</summary>
/// <param name="FrameCount">The number of frames measured.</param>
/// <param name="Average">The average frame time.</param>
/// <param name="Median">The median frame time.</param>
/// <param name="Percentile95">The 95th percentile frame time.</param>
/// <param name="Maximum">The longest frame time.</param>
/// <param name="SlowFrames">Frames over 16.7 ms (one frame at 60 Hz).</param>
/// <param name="VerySlowFrames">Frames over 33.3 ms (two frames at 60 Hz).</param>
public sealed record CarMapFrameStatistics(
    int FrameCount, TimeSpan Average, TimeSpan Median, TimeSpan Percentile95, TimeSpan Maximum, int SlowFrames, int VerySlowFrames)
{
    /// <summary>Gets statistics without frames.</summary>
    public static CarMapFrameStatistics Empty { get; } = new(0, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, 0, 0);
}

/// <summary>Draws the map with <c>Microsoft.Maui.Graphics</c>; the same code runs on CarPlay and Android Auto.</summary>
public interface ICarSurfaceRenderer
{
    /// <summary>
    /// Draws one frame on the main thread after <see cref="ICarMapSurface.Invalidate"/> or a size or area change. The canvas
    /// covers the whole surface in device-independent units; keep important content inside <paramref name="stableArea"/>.
    /// </summary>
    /// <param name="canvas">The canvas.</param>
    /// <param name="visibleArea">The part of the surface the car does not cover with its own UI right now.</param>
    /// <param name="stableArea">The part of the surface the car's UI never covers.</param>
    void Draw(ICanvas canvas, RectF visibleArea, RectF stableArea);
}

/// <summary>
/// The map on one car display. Its content is set once (a renderer, a native view or a MAUI view) and shows every time the
/// car connects; sizes and gestures are in device-independent units (iOS points, Android dp). Events run on the main thread.
/// </summary>
public interface ICarMapSurface
{
    /// <summary>Gets the display.</summary>
    CarMapSurfaceKind Kind { get; }

    /// <summary>Gets a value indicating whether the car currently shows the surface.</summary>
    bool IsAvailable { get; }

    /// <summary>Gets the surface size.</summary>
    SizeF Size { get; }

    /// <summary>Gets the pixels per device-independent unit.</summary>
    float Density { get; }

    /// <summary>Gets the part of the surface the car does not cover with its own UI right now.</summary>
    RectF VisibleArea { get; }

    /// <summary>Gets the part of the surface the car's UI never covers.</summary>
    RectF StableArea { get; }

    /// <summary>Gets the appearance of the car display.</summary>
    CarContentStyle ContentStyle { get; }

    /// <summary>Gets a value indicating whether the driver is panning the map (Android Auto pan mode, CarPlay panning interface).</summary>
    bool IsPanning { get; }

    /// <summary>Gets the frame times of the recent frames.</summary>
    CarMapFrameStatistics FrameStatistics { get; }

    /// <summary>Raised when <see cref="IsAvailable"/> changes.</summary>
    event EventHandler AvailabilityChanged;

    /// <summary>Raised when <see cref="Size"/>, <see cref="VisibleArea"/> or <see cref="StableArea"/> changes.</summary>
    event EventHandler AreasChanged;

    /// <summary>Raised when <see cref="IsPanning"/> changes.</summary>
    event EventHandler PanModeChanged;

    /// <summary>Raised when the driver scrolls, scales, flings or taps the map.</summary>
    event EventHandler<CarMapGestureEventArgs> Gesture;

    /// <summary>Draws the map with <paramref name="renderer"/> directly on the car surface (on Android without a virtual display).</summary>
    /// <param name="renderer">The renderer.</param>
    void UseRenderer(ICarSurfaceRenderer renderer);

#if IOS
    /// <summary>Shows a native map, such as MapKit, as the root view controller of the car window; the factory runs for every window.</summary>
    /// <param name="factory">Creates the view controller.</param>
    void UseViewController(Func<UIKit.UIViewController> factory);
#elif ANDROID
    /// <summary>
    /// Shows a native map view in a <c>Presentation</c> on a virtual display over the car surface; the factory runs for every
    /// surface with the presentation's context.
    /// </summary>
    /// <param name="factory">Creates the view.</param>
    void UseView(Func<Android.Content.Context, Android.Views.View> factory);
#endif

    /// <summary>Shows a MAUI view (<c>View.ToPlatform</c>) on the car display; the factory runs for every window or surface. No performance guarantees.</summary>
    /// <param name="factory">Creates the view.</param>
    [Experimental("CARKIT0002")]
    void UseMauiView(Func<Microsoft.Maui.Controls.View> factory);

    /// <summary>Removes the content.</summary>
    void ClearContent();

    /// <summary>Requests a new frame of the renderer; several requests before the next frame draw once.</summary>
    void Invalidate();

    /// <summary>Clears <see cref="FrameStatistics"/>.</summary>
    void ResetFrameStatistics();
}

/// <summary>The map surfaces of the car displays, from <c>car.UseNavigation(...)</c> in dependency injection.</summary>
public interface ICarMapSurfaces
{
    /// <summary>Gets the main car screen.</summary>
    ICarMapSurface CarScreen { get; }

    /// <summary>Gets the CarPlay Dashboard.</summary>
    ICarMapSurface Dashboard { get; }

    /// <summary>Gets the instrument cluster.</summary>
    ICarMapSurface InstrumentCluster { get; }
}

/// <summary>Speaks navigation prompts over the car speakers, ducking other audio while it speaks.</summary>
public interface ICarVoicePrompts
{
    /// <summary>Gets a value indicating whether a prompt is being spoken.</summary>
    bool IsSpeaking { get; }

    /// <summary>
    /// Queues a prompt; prompts play one after another. The task completes when the prompt has been spoken, is cancelled
    /// (<see cref="OperationCanceledException"/>) by <paramref name="cancellationToken"/>, <see cref="CancelAll"/> or the car
    /// ending navigation, and fails with <see cref="CarPlatformException"/> when the speech engine or audio focus fails.
    /// </summary>
    /// <param name="text">The prompt.</param>
    /// <param name="cancellationToken">Cancels the prompt, also while it is spoken.</param>
    /// <returns>A task that completes when the prompt has been spoken.</returns>
    Task SpeakAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>Stops the current prompt and cancels the queued ones.</summary>
    void CancelAll();
}
