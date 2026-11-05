using CarKit.Maui.Renderers;
using CarPlay;
using Foundation;

namespace CarKit.Maui.Media;

// CarNowPlayingTemplate → CPNowPlayingTemplate.SharedTemplate (spec: "Media module decisions (Stage 5)"):
// CPNowPlaying*Button buttons with selection from the player state (UpdateNowPlayingButtons after every change),
// Up Next, Album/Artist and an observer that calls the model handlers. There is a single shared template, so the
// renderer keeps one current state; updates always happen in place. Stage 9: CarKit sets NowPlayingMode only for
// Sports (iOS 18.4) and returns to the default mode once it is removed; without Sports the mode is left alone for the
// escape hatch. MiniPlayer (iOS 27) is a gate (log in the validator).
internal sealed class NowPlayingTemplateRenderer : ICarPlayModuleRenderer, IDisposable
{
    private readonly CarMediaRuntime _runtime;
    private readonly Observer _observer;
    private (CarPlayTemplateState State, CarPlayRenderContext Context)? _current;
    private IReadOnlyList<CarResolvedNowPlayingButton>? _shownButtons;
    private bool _observing;
    // Stage 9: sports mode set by CarKit (model and image theme), for comparison on update
    private (CarNowPlayingSports Model, CarContentStyle Style)? _shownSports;

    public NowPlayingTemplateRenderer(CarMediaRuntime runtime)
    {
        _runtime = runtime;
        _observer = new Observer(this);
        runtime.Controller.StateChanged += OnStateChanged;
    }

    public bool CanRender(CarTemplate template) => template is CarNowPlayingTemplate;

    public CarPlayTemplateState Render(CarTemplate template, CarPlayRenderContext context)
    {
        var model = (CarNowPlayingTemplate)template;
        var native = CPNowPlayingTemplate.SharedTemplate;
        if (context.IsShown(native) && native.UserInfo is NSString shown && shown.ToString() != model.Id)
        {
            throw new InvalidOperationException(
                $"CarPlay shows one Now Playing screen; '{shown}' is already shown. Pop it, or update it with UpdateAsync.");
        }

        if (!_observing)
        {
            native.AddObserver(_observer);
            _observing = true;
        }

        var state = new CarPlayTemplateState(model) { Native = native };
        Apply(state, native, model, context);
        return state;
    }

    public bool UpdateInPlace(CarPlayTemplateState state, CarTemplate updated, CarPlayRenderContext context)
    {
        Apply(state, (CPNowPlayingTemplate)state.Native, (CarNowPlayingTemplate)updated, context);
        return true;
    }

    public void Dispose()
    {
        _runtime.Controller.StateChanged -= OnStateChanged;
        if (_observing)
        {
            CPNowPlayingTemplate.SharedTemplate.RemoveObserver(_observer);
        }

        _observer.Dispose();
    }

    // Also from device tests (CarPlay observer events)
    internal void OnUpNext() => Select(model => model.OnUpNextSelected, "UpNext");

    internal void OnAlbumArtist() => Select(model => model.OnAlbumArtistSelected, "AlbumArtist");

    private void Apply(CarPlayTemplateState state, CPNowPlayingTemplate native, CarNowPlayingTemplate model, CarPlayRenderContext context)
    {
        _current = (state, context);
        native.IsUpNextButtonEnabled = model.IsUpNextButtonEnabled;
        if (model.UpNextTitle is { } title)
        {
            native.UpNextTitle = title.Text;
        }

        native.IsAlbumArtistButtonEnabled = model.IsAlbumArtistButtonEnabled;
        ApplySports(state, native, model, context, force: false);
        UpdateButtons(state, native, model, context, force: true);
    }

    // Stage 9: sports mode (iOS 18.4); the gate removes Sports below 18.4 and on Android Auto
    private void ApplySports(CarPlayTemplateState state, CPNowPlayingTemplate native, CarNowPlayingTemplate model, CarPlayRenderContext context, bool force)
    {
        if (!OperatingSystem.IsIOSVersionAtLeast(18, 4))
        {
            return;
        }

        if (Sports(model, context) is { } sports)
        {
            if (force || _shownSports != (sports, context.Style))
            {
                native.NowPlayingMode = CarPlaySportsMode.Create(sports, state, context, ReloadSports);
                _shownSports = (sports, context.Style);
            }
        }
        else if (_shownSports is not null)
        {
            native.NowPlayingMode = CPNowPlayingMode.DefaultNowPlayingMode;
            _shownSports = null;
        }
    }

    // Sports of the template (after the validator gate) or of the playing item (CarMediaItem.Sports; a session gate,
    // because the item does not go through the validator)
    private CarNowPlayingSports? Sports(CarNowPlayingTemplate model, CarPlayRenderContext context) =>
        model.Sports ?? (context.Session.Capabilities.Supports(CarFeature.NowPlayingSportsMode) ? _runtime.Controller.State.Item?.Sports : null);

    // A late sports mode image: a new mode from the cache for the current model
    private void ReloadSports()
    {
        if (Current() is { State: var state, Context: var context } && state.Model is CarNowPlayingTemplate model)
        {
            ApplySports(state, (CPNowPlayingTemplate)state.Native, model, context, force: true);
            CarPlayHooks.Apply(model, state.Native, context);
        }
    }

    // true = buttons sent again
    private bool UpdateButtons(CarPlayTemplateState state, CPNowPlayingTemplate native, CarNowPlayingTemplate model, CarPlayRenderContext context, bool force)
    {
        var resolved = CarNowPlayingButtons.Resolve(model, _runtime.Controller.State);
        if (!force && _shownButtons is { } shown && shown.SequenceEqual(resolved))
        {
            return false;
        }

        _shownButtons = resolved;
        native.UpdateNowPlayingButtons([.. resolved.Select(button => Button(state, button, context))]);
        return true;
    }

    private CPNowPlayingButton Button(CarPlayTemplateState state, CarResolvedNowPlayingButton resolved, CarPlayRenderContext context)
    {
        var model = resolved.Button;
        void Handler(CPNowPlayingButton _) => Press(state, model, context);
        CPNowPlayingButton native = model.Kind switch
        {
            CarNowPlayingButtonKind.Shuffle => new CPNowPlayingShuffleButton(Handler),
            CarNowPlayingButtonKind.Repeat => new CPNowPlayingRepeatButton(Handler),
            CarNowPlayingButtonKind.PlaybackRate => new CPNowPlayingPlaybackRateButton(Handler),
            CarNowPlayingButtonKind.AddToLibrary => new CPNowPlayingAddToLibraryButton(Handler),
            CarNowPlayingButtonKind.More => new CPNowPlayingMoreButton(Handler),
            _ => new CPNowPlayingImageButton(
                context.Image(state, "now-playing:" + model.Id, model.Image, CPNowPlayingButton.MaximumImageSize, _ => Refresh())
                    ?? context.Placeholder(CPNowPlayingButton.MaximumImageSize),
                Handler),
        };
        native.Enabled = resolved.IsEnabled;
        native.Selected = resolved.IsSelected;
        return native;
    }

    // Handler of the current version of a button from the state model; without a handler, the controller's default
    // action (shuffle, repeat, rate, custom)
    private void Press(CarPlayTemplateState state, CarNowPlayingButton rendered, CarPlayRenderContext context)
    {
        var button = state.Current(rendered);
        if (button.OnSelected is { } handler)
        {
            context.Run(handler, context.Session.CreateContext(button), "Select");
            return;
        }

        _runtime.Controller.PressAsync(button).Forget(context.Errors, "Media.NowPlayingButton");
    }

    private void Select(Func<CarNowPlayingTemplate, Func<CarItemContext, Task>?> handler, string operation)
    {
        if (Current() is { State.Model: CarNowPlayingTemplate model, Context: var context })
        {
            context.Run(handler(model), context.Session.CreateContext(model), operation);
        }
    }

    // Shuffle/Repeat selection follows the player state, and a button image may arrive later
    private void OnStateChanged(CarPlaybackState _)
    {
        if (Current() is not { State: var state, Context: var context } || state.Model is not CarNowPlayingTemplate model)
        {
            return;
        }

        // Stage 9: sports mode of the playing item (the item or its score changed)
        var previousSports = _shownSports;
        ApplySports(state, (CPNowPlayingTemplate)state.Native, model, context, force: false);
        if (UpdateButtons(state, (CPNowPlayingTemplate)state.Native, model, context, force: false) || previousSports != _shownSports)
        {
            // New buttons or mode: the ConfigureCarPlay hooks run again (escape hatch contract, review M3)
            CarPlayHooks.Apply(model, state.Native, context);
        }
    }

    private void Refresh()
    {
        if (Current() is { State: var state, Context: var context } && state.Model is CarNowPlayingTemplate model)
        {
            UpdateButtons(state, (CPNowPlayingTemplate)state.Native, model, context, force: true);
            CarPlayHooks.Apply(model, state.Native, context);
        }
    }

    // After CarPlay disconnects, the state and session of the previous connection are released (no reference to
    // CarSession)
    private (CarPlayTemplateState State, CarPlayRenderContext Context)? Current()
    {
        if (_current is { Context.Session.IsConnected: false })
        {
            _current = null;
            _shownButtons = null;
            _shownSports = null;
        }

        return _current;
    }

    // The observer is weak on the CarPlay side; the renderer (a DI singleton) holds it for the whole lifetime of the
    // app
    private sealed class Observer(NowPlayingTemplateRenderer renderer) : NSObject, ICPNowPlayingTemplateObserver
    {
        [Export("nowPlayingTemplateUpNextButtonTapped:")]
        public void UpNextButtonTapped(CPNowPlayingTemplate nowPlayingTemplate) => renderer.OnUpNext();

        [Export("nowPlayingTemplateAlbumArtistButtonTapped:")]
        public void AlbumArtistButtonTapped(CPNowPlayingTemplate nowPlayingTemplate) => renderer.OnAlbumArtist();
    }
}
