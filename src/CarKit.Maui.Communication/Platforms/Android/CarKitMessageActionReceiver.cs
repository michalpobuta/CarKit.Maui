using Android.Content;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;

namespace CarKit.Maui.Communication;

/// <summary>
/// Receives the Reply and Mark as read actions of CarKit message notifications and passes them to
/// <see cref="ICarMessageHandler"/>. CarKit generates the registered subclass for apps with the Communication category.
/// </summary>
public abstract class CarKitMessageActionReceiver : BroadcastReceiver
{
    /// <summary>Creates the receiver.</summary>
    protected CarKitMessageActionReceiver()
    {
    }

    /// <inheritdoc />
    public sealed override void OnReceive(Context? context, Intent? intent)
    {
        if (intent?.Action is not { } action || intent.GetStringExtra(CarAndroidMessageNotifier.ConversationIdExtra) is not { Length: > 0 } conversationId)
        {
            return;
        }

        // MauiApplication.OnCreate runs before the receiver, also when the process started only for a notification
        // action
        var services = IPlatformApplication.Current?.Services;
        if (services?.GetService<CarAndroidMessageNotifier>() is not { } notifier)
        {
            return;
        }

        // goAsync: the app handler may wait for the network; the system gives the receiver about 10 s
        var pending = GoAsync();
        _ = Run(notifier, action, conversationId, intent, pending, services.GetRequiredService<ICarErrorHandler>());
    }

    private static async Task Run(CarAndroidMessageNotifier notifier, string action, string conversationId, Intent intent, PendingResult? pending, ICarErrorHandler errors)
    {
        try
        {
            await notifier.HandleActionAsync(action, conversationId, intent);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            errors.HandleError(ex, new CarErrorContext(action == CarAndroidMessageNotifier.ReplyAction ? CarMessageRouter.ReplyOperation : CarMessageRouter.MarkAsReadOperation, null));
        }
        finally
        {
            pending?.Finish();
        }
    }
}
