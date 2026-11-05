using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CarKit.Maui.Communication;

/// <summary>Configures the communication module inside <see cref="CarKitCommunicationBuilderExtensions.UseCommunication(CarKitBuilder, Action{CarCommunicationBuilder})"/>.</summary>
public sealed class CarCommunicationBuilder
{
    internal CarCommunicationBuilder(IServiceCollection services) => Services = services;

    /// <summary>Gets the app services.</summary>
    public IServiceCollection Services { get; }

    /// <summary>Gets or sets the name of the Android notification channel for messages. Defaults to "Messages".</summary>
    public string NotificationChannelName { get; set; } = "Messages";

    /// <summary>Gets or sets the title of the Reply notification action. Defaults to "Reply".</summary>
    public string ReplyActionTitle { get; set; } = "Reply";

    /// <summary>Gets or sets the title of the Mark as read notification action. Defaults to "Mark as read".</summary>
    public string MarkAsReadActionTitle { get; set; } = "Mark as read";

    /// <summary>Gets or sets the drawable resource name of the Android notification icon. Defaults to the app icon.</summary>
    public string? AndroidSmallIcon { get; set; }

    /// <summary>Gets or sets a value indicating whether calls appear in the system Recents list. Defaults to <see langword="true"/>.</summary>
    public bool IncludeCallsInRecents { get; set; } = true;

    internal Action<IServiceCollection>? MessageHandlerRegistration { get; private set; }

    internal Action<IServiceCollection>? CallHandlerRegistration { get; private set; }

    /// <summary>
    /// Registers the app's message handler as a singleton; <see cref="ICarMessageHandler"/> resolves to the same instance as
    /// <typeparamref name="THandler"/>. A later call replaces an earlier one.
    /// </summary>
    /// <typeparam name="THandler">The handler type, created by dependency injection.</typeparam>
    /// <returns>This builder.</returns>
    public CarCommunicationBuilder UseMessageHandler<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] THandler>()
        where THandler : class, ICarMessageHandler
    {
        MessageHandlerRegistration = static services =>
        {
            services.TryAddSingleton<THandler>();
            services.AddSingleton<ICarMessageHandler>(static sp => sp.GetRequiredService<THandler>());
        };
        return this;
    }

    /// <summary>
    /// Registers the app's call handler as a singleton; <see cref="ICarCallHandler"/> resolves to the same instance as
    /// <typeparamref name="THandler"/>. A later call replaces an earlier one.
    /// </summary>
    /// <typeparam name="THandler">The handler type, created by dependency injection.</typeparam>
    /// <returns>This builder.</returns>
    public CarCommunicationBuilder UseCallHandler<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] THandler>()
        where THandler : class, ICarCallHandler
    {
        CallHandlerRegistration = static services =>
        {
            services.TryAddSingleton<THandler>();
            services.AddSingleton<ICarCallHandler>(static sp => sp.GetRequiredService<THandler>());
        };
        return this;
    }

    internal CarCommunicationOptions Build(string? appGroup)
    {
        foreach (var (name, value) in new[]
        {
            (nameof(NotificationChannelName), NotificationChannelName),
            (nameof(ReplyActionTitle), ReplyActionTitle),
            (nameof(MarkAsReadActionTitle), MarkAsReadActionTitle),
        })
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"communication.{name} cannot be empty.");
            }
        }

        return new CarCommunicationOptions(
            NotificationChannelName,
            ReplyActionTitle,
            MarkAsReadActionTitle,
            string.IsNullOrWhiteSpace(AndroidSmallIcon) ? null : AndroidSmallIcon,
            IncludeCallsInRecents,
            string.IsNullOrWhiteSpace(appGroup) ? null : appGroup.Trim());
    }
}

// Module configuration after UseCommunication (immutable). AppGroup comes from the MSBuild <CarKitIosAppGroup>
// (Intents Extension store)
internal sealed record CarCommunicationOptions(
    string NotificationChannelName,
    string ReplyActionTitle,
    string MarkAsReadActionTitle,
    string? AndroidSmallIcon,
    bool IncludeCallsInRecents,
    string? AppGroup);
