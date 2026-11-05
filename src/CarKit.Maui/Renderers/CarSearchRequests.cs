namespace CarKit.Maui.Renderers;

// Successive search texts being typed: results of an older query that returned later do not overwrite newer ones.
// Main thread only (CarPlay and Car App Library callbacks).
internal sealed class CarSearchRequests
{
    private int _latest;

    public string Text { get; private set; } = string.Empty;

    public int Begin(string text)
    {
        Text = text;
        return ++_latest;
    }

    public bool IsLatest(int request) => request == _latest;
}
