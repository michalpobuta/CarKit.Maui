using Android.Content;
using Android.Content.Res;
using AndroidX.Car.App;
using Screen = AndroidX.Car.App.Screen;
using AndroidX.Lifecycle;

namespace CarKit.Maui;

// One Car App Library session = one CarKit connection. Session ON_DESTROY = car disconnect.
internal sealed class CarKitSession : Session, ILifecycleEventObserver
{
    private readonly CarKitRuntime _runtime;
    private CarAppLibraryHost? _host;
    private CarKitConnection? _connection;

    public CarKitSession(CarKitRuntime runtime)
    {
        _runtime = runtime;
        Lifecycle!.AddObserver(this);
    }


    public override Screen OnCreateScreen(Intent? intent)
    {
        var host = new CarAppLibraryHost(CarContext!, _runtime);
        var root = host.CreateRootScreen();
        var connection = _runtime.Connect(host);
        host.Attach(connection.Session);
        _host = host;
        _connection = connection;
        // Car App Library pushes the main screen onto the stack only after OnCreateScreen returns; OnConnectedAsync
        // (e.g. restoring the stack through PushAsync) starts on the next turn of the loop, when the root is already on
        // the stack
        host.Events.Post(() => connection.StartAsync().Forget(_runtime.ErrorHandler, "OnConnected"));
        // Stage 8: the start intent (e.g. ACTION_NAVIGATE from the Assistant) after the connection starts, for modules
        // (Navigation)
        if (intent is not null)
        {
            host.Events.Post(() => host.OnIntent(intent));
        }

        return root;
    }

    public override void OnCarConfigurationChanged(Configuration? newConfiguration) => _host?.OnCarConfigurationChanged();

    public override void OnNewIntent(Intent? intent)
    {
        if (intent is not null)
        {
            _host?.OnIntent(intent);
        }
    }

    public void OnStateChanged(ILifecycleOwner source, Lifecycle.Event e)
    {
        if (!Equals(e, Lifecycle.Event.OnDestroy))
        {
            return;
        }

        // getLifecycle() is the session's public registry: ON_DESTROY arrives here after ScreenManager destroys the screens
        // (screen reports are deferred by CarHostEvents and Close() invalidates them)
        Lifecycle!.RemoveObserver(this);
        var connection = _connection;
        _host?.Close();
        _host = null;
        _connection = null;
        connection?.DisconnectAsync().Forget(_runtime.ErrorHandler, "OnDisconnected");
    }
}
