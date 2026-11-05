using Android.Runtime;
using AndroidX.Car.App.Messaging.Model;
using AndroidX.Car.App.Model;
using AndroidX.Core.App;
using Java.Lang.Reflect;

namespace CarKit.Maui.Renderers;

// CarConversationItem → ConversationItem (Car App API 7, spec: "Communication module decisions (Stage 6)") in a
// ListTemplate. The 1.7.0.1 binding types ConversationItem.Builder.SetConversationCallback with an AIDL interface
// (IConversationCallback), while Java expects androidx.car.app.messaging.model.ConversationCallback; we call the method
// through JNI from a java.lang.reflect.Proxy of that interface (no .NET reflection and no custom binding). Below API 7
// RowRenderer provides a Row fallback.
internal static class ConversationRenderer
{
    private const string CallbackInterface = "androidx.car.app.messaging.model.ConversationCallback";
    private const string SetCallbackSignature = "(Landroidx/car/app/messaging/model/ConversationCallback;)Landroidx/car/app/messaging/model/ConversationItem$Builder;";

    private static IntPtr _setCallback;
    private static Java.Lang.Class? _callbackClass;

    // A requirement from the platform rules (items.Conversation in androidauto.json): below it, the Row fallback
    public static bool Supported(CarAppRenderContext context) =>
        !CarPlatformRules.For(CarPlatform.AndroidAuto).Items.TryGetValue(CarListItemKinds.Conversation, out var requirement)
        || requirement.MinApiLevel is not { } minimum
        || context.ApiLevel >= minimum;

    public static (IItem Item, CarAppRowShape Shape) Render(CarConversationItem model, CarAppRenderContext context, CarAppScreenState screen)
    {
        var self = Person(model.Self);
        var messages = new JavaList<CarMessage>();
        foreach (var message in model.Messages)
        {
            messages.Add(new CarMessage.Builder()
                .SetSender(message.Sender is { } sender ? Person(sender) : self)!
                .SetBody(CarAppTemplateRenderers.Text(message.Text.Length > 0 ? message.Text : " "))!
                .SetReceivedTimeEpochMillis(message.Timestamp.ToUnixTimeMilliseconds())!
                .SetRead(message.IsRead)!
                .Build()!);
        }

#pragma warning disable CS0618 // The constructor with a callback types the AIDL interface in the binding; the parameterless one (@Deprecated in 1.7) works, and we set the callback through JNI
        var builder = new ConversationItem.Builder();
#pragma warning restore CS0618
        _ = builder.SetId(model.Id)!
            .SetTitle(CarAppTemplateRenderers.Text(model.Title.Text.Length > 0 ? model.Title : RowRenderer.Title(model.Title)))!
            .SetSelf(self)!
            .SetGroupConversation(model.IsGroupConversation)!
            .SetMessages(messages);
        if (context.Icon(screen, model.Image, CarAppImages.SmallPixels) is { } icon)
        {
            builder.SetIcon(icon);
        }

        SetCallback(builder, new ConversationCallback(context, model));
        return (builder.Build()!, new CarAppRowShape(RowRenderer.Title(model.Title)));
    }

    // Person requires a name and a key (ConversationItem.validateSender); the uri helps the host match the contact
    public static Person Person(CarPerson person)
    {
        var builder = new Person.Builder().SetKey(person.Key)!.SetName(person.Name)!;
        if (person.PhoneOrEmail is { Length: > 0 } handle)
        {
            builder.SetUri((handle.Contains('@', StringComparison.Ordinal) ? "mailto:" : "tel:") + handle);
        }

        return builder.Build()!;
    }

    private static void SetCallback(ConversationItem.Builder builder, ConversationCallback handler)
    {
        if (_setCallback == IntPtr.Zero)
        {
            var type = JNIEnv.FindClass(typeof(ConversationItem.Builder));
            try
            {
                _setCallback = JNIEnv.GetMethodID(type, "setConversationCallback", SetCallbackSignature);
            }
            finally
            {
                JNIEnv.DeleteGlobalRef(type);
            }
        }

        // Class.forName with the app's class loader (a call from JNI without Java frames would search the system loader)
        var callbackClass = _callbackClass ??= Java.Lang.Class.ForName(CallbackInterface, true, Java.Lang.Class.FromType(typeof(ConversationItem)).ClassLoader);
        using var proxy = Proxy.NewProxyInstance(callbackClass.ClassLoader, [callbackClass], handler)!;
        var result = JNIEnv.CallObjectMethod(builder.Handle, _setCallback, new JValue(proxy));
        JNIEnv.DeleteLocalRef(result);
    }

    // Calls on the ConversationCallback proxy: onTextReply/onMarkAsRead (the host, after reading out and replying by voice)
    // and the Object methods (hashCode/equals/toString), which the proxy also passes to the handler. Main thread
    // (RemoteUtils.dispatchCallFromHost). Every screen rebuild creates a new callback with the current model (like the
    // rows' OnClickListener).
    private sealed class ConversationCallback(CarAppRenderContext context, CarConversationItem rendered) : Java.Lang.Object, IInvocationHandler
    {
        public Java.Lang.Object? Invoke(Java.Lang.Object? proxy, Method? method, Java.Lang.Object[]? args)
        {
            switch (method?.Name)
            {
                case "onTextReply":
                    var text = args is { Length: > 0 } ? args[0]?.ToString() ?? string.Empty : string.Empty;
                    if (rendered.OnReply is null)
                    {
                        CarKitRenderLog.ConversationHandlerMissing(context.Logger, "onTextReply", rendered.Id);
                    }

                    context.Run(rendered.OnReply, new CarTextInputContext(context.Session, rendered, text, context.Session.ConnectionToken), "Reply");
                    return null;
                case "onMarkAsRead":
                    if (rendered.OnMarkAsRead is null)
                    {
                        CarKitRenderLog.ConversationHandlerMissing(context.Logger, "onMarkAsRead", rendered.Id);
                    }

                    context.Run(rendered.OnMarkAsRead, context.Session.CreateContext(rendered), "MarkAsRead");
                    return null;
                case "hashCode":
                    return Java.Lang.Integer.ValueOf(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this));
                case "equals":
                    return Java.Lang.Boolean.ValueOf(proxy is not null && args is [{ } other, ..] && JNIEnv.IsSameObject(proxy.Handle, other.Handle));
                case "toString":
                    return new Java.Lang.String("CarKitConversationCallback(" + rendered.Id + ")");
                default:
                    return null;
            }
        }
    }
}
