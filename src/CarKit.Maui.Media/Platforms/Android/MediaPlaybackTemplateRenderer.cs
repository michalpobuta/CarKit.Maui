using Android.Runtime;
using AndroidX.Car.App;
using AndroidX.Car.App.Media;
using AndroidX.Car.App.Media.Model;
using AndroidX.Media3.Session;
using CarKit.Maui.Renderers;
using MediaSessionCompat = Android.Support.V4.Media.Session.MediaSessionCompat;

namespace CarKit.Maui.Media;

// Android Auto media templates behind media.UseTemplatedMediaOnAndroid (spec: "Media module decisions (Stage 5)"):
// CarNowPlayingTemplate → MediaPlaybackTemplate (Car App API 8) with a header and hooks, the Media3 session token in
// MediaPlaybackManager on connection. Without the flag there is no renderer (Android Auto draws playback itself).
// Main thread.
internal sealed class MediaPlaybackTemplateRenderer(CarMediaRuntime runtime) : ICarAppModuleRenderer
{
    public const int ApiLevel = 8;

    public bool CanRender(CarTemplate template, CarAppRenderContext context) => template is CarNowPlayingTemplate && runtime.Options.TemplatedMedia;

    public CarAppRendered Render(CarTemplate template, CarAppRenderContext context, CarAppScreenState screen)
    {
        var model = (CarNowPlayingTemplate)template;
        var title = model.Title?.Text ?? runtime.Controller.State.Item?.Title;
        var builder = new MediaPlaybackTemplate.Builder();
        if (!screen.InTab)
        {
            builder.SetHeader(HeaderRenderer.Header(title, model, screen, context));
        }

        CarAppHooks.Apply(model, builder, context);
        return new CarAppRendered(builder.Build()!, new CarAppTemplateShape(CarAppTemplateTypes.MediaPlayback, title, IsLoading: false));
    }

    // Template requirement: registerMediaPlaybackToken; the session may appear only after the car connects (the
    // service is created later)
    public void OnConnected(CarAppRenderContext context, Java.Lang.Object carContext)
    {
        if (!runtime.Options.TemplatedMedia || context.ApiLevel < ApiLevel || carContext is not CarContext car)
        {
            return;
        }

        if (CarMediaSessions.Current is { } session)
        {
            Register(car, session, context);
            return;
        }

        void OnOpened(MediaSession opened)
        {
            CarMediaSessions.SessionOpened -= OnOpened;
            Register(car, opened, context);
        }

        CarMediaSessions.SessionOpened += OnOpened;
    }

    private static void Register(CarContext car, MediaSession session, CarAppRenderContext context)
    {
        try
        {
            var manager = car.GetCarService(Java.Lang.Class.FromType(typeof(MediaPlaybackManager)))!.JavaCast<MediaPlaybackManager>();
            manager.RegisterMediaPlaybackToken(MediaSessionCompat.Token.FromToken(session.PlatformToken!)!);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            context.Errors.HandleError(ex, new CarErrorContext("Media.RegisterMediaPlaybackToken", null));
        }
    }
}
