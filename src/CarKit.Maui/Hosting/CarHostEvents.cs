namespace CarKit.Maui;

// Native host events (back, dismiss, theme) and the connection start run on the next turn of the main loop,
// and are dropped after Close(). Car App Library destroys screens before the session's ON_DESTROY and pushes the
// main screen onto the stack only after OnCreateScreen; deferral separates these sequences from session
// operations. Main thread only.
internal sealed class CarHostEvents(Action<Action> post)
{
    public bool IsClosed { get; private set; }

    public void Post(Action work)
    {
        ArgumentNullException.ThrowIfNull(work);
        if (IsClosed)
        {
            return;
        }

        post(() =>
        {
            if (!IsClosed)
            {
                work();
            }
        });
    }

    public void Close() => IsClosed = true;
}
