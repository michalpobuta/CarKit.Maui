using CarKit.Maui;

namespace CarKit.Sample.Car;

// Stage 3: menu of all shared templates (SampleScreens) on both platforms. The stack (without the root) remembered on
// disconnect is restored after reconnecting (ICarSession.Stack stays readable after disconnect). Stage 7: categories
// from the build (CarKitOptions in DI) select the flows in the menu; the conversational app starts with voice.
public sealed class SampleCarAppHandler(CarKitOptions options) : ICarAppHandler
{
    // Ids of the screens above the root at disconnect
    private string[] _restore = [];

    public async Task OnConnectedAsync(ICarSession session, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(session);
        SampleDiagnostics.CarConnected(session.Platform);
        var screens = new SampleScreens(session, options.Categories);
        await session.SetRootAsync(screens.Menu());
        if (screens.StartsWithVoice)
        {
            await screens.PresentAssistantAsync(ct);
            return;
        }

        foreach (var id in _restore)
        {
            if (screens.Rebuild(id) is not { } template)
            {
                break;
            }

            await session.PushAsync(template);
        }
    }

    public Task OnDisconnectedAsync(ICarSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _restore = [.. session.Stack.Skip(1).Select(template => template.Id)];
        SampleDiagnostics.CarDisconnected();
        return Task.CompletedTask;
    }
}
