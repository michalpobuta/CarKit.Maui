namespace CarKit.Maui;

// Platform SPI: implemented by the renderers (CarPlay, Car App Library) and FakeCarHost.
// The session calls these methods on the main thread and passes templates after validation, fallback and truncation.
// The host reports host events (back, dismiss, theme, disconnect) through the internal CarSession methods.
//
// Contract for implementations:
// - The session holds its gate for the whole ICarHost call. The host must not wait inside the call on
//   CarSession (OnHostPoppedAsync, OnHostDismissedAsync, OnContentStyleChangedAsync, InvokeAsync) — deadlock.
//   Native events that arrive in the meantime are reported without waiting (fire-and-forget); the session matches
//   them by Id.
// - Exception: a modal that the host closes itself as part of the current operation (Android Auto: a modal screen on
//   push or a new root) is reported synchronously through CarSession.OnHostClosedModalDuringOperation (without the
//   gate).
// - The returned task must complete or fail also after the car disconnects (e.g. a CarPlay completion handler that
//   will never arrive): a disconnect ends pending calls with CarNotConnectedException.
// - The host lets native exceptions through; the session wraps them in CarPlatformException.
internal interface ICarHost
{
    CarPlatform Platform { get; }

    ICarCapabilities Capabilities { get; }

    CarContentStyle ContentStyle { get; }

    Task SetRootAsync(CarTemplate root, bool animated);

    Task PushAsync(CarTemplate template, bool animated);

    Task PopAsync(bool animated);

    Task PopToAsync(CarTemplate template, bool animated);

    Task PopToRootAsync(bool animated);

    Task PresentAsync(CarTemplate modal, bool animated);

    Task DismissAsync(bool animated);

    Task UpdateAsync(CarTemplate previous, CarTemplate updated);

    TNative? GetNative<TNative>() where TNative : class;
}
