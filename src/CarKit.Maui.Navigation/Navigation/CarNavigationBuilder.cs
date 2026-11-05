using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CarKit.Maui.Navigation;

/// <summary>Configures the navigation module inside <see cref="CarKitNavigationBuilderExtensions.UseNavigation"/>.</summary>
public sealed class CarNavigationBuilder
{
    internal CarNavigationBuilder(IServiceCollection services) => Services = services;

    /// <summary>Gets the app services.</summary>
    public IServiceCollection Services { get; }

    /// <summary>Gets or sets the language of voice prompts as a BCP-47 tag, such as <c>en-GB</c>; defaults to the system language.</summary>
    public string? VoicePromptLanguage { get; set; }

    /// <summary>Gets or sets a value indicating whether map surfaces log their frame times every 120 frames (category <c>CarKit.Navigation</c>).</summary>
    public bool LogFrameTimes { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether CarPlay shares the route, its legs and waypoints with the car's built-in
    /// navigation (iOS 26.4, cars that support it; <see cref="CarFeature.RouteSharing"/>). The car may then propose stops and
    /// destinations through <see cref="ICarNavigationHandler"/>. Off by default.
    /// </summary>
    public bool ShareRoutesWithCar { get; set; }

    internal Action<IServiceCollection>? HandlerRegistration { get; private set; }

    /// <summary>
    /// Registers the app's navigation handler as a singleton; <see cref="ICarNavigationHandler"/> resolves to the same instance
    /// as <typeparamref name="THandler"/>. A later call replaces an earlier one.
    /// </summary>
    /// <typeparam name="THandler">The handler type, created by dependency injection.</typeparam>
    /// <returns>This builder.</returns>
    public CarNavigationBuilder UseHandler<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] THandler>()
        where THandler : class, ICarNavigationHandler
    {
        HandlerRegistration = static services =>
        {
            services.TryAddSingleton<THandler>();
            services.AddSingleton<ICarNavigationHandler>(static sp => sp.GetRequiredService<THandler>());
        };
        return this;
    }

    internal CarNavigationOptions Build() =>
        new(string.IsNullOrWhiteSpace(VoicePromptLanguage) ? null : VoicePromptLanguage.Trim(), LogFrameTimes) { ShareRoutesWithCar = ShareRoutesWithCar };
}

internal sealed record CarNavigationOptions(string? VoicePromptLanguage, bool LogFrameTimes)
{
    // Stage 9: mapTemplateShouldProvideRouteSharing: (CarPlay iOS 26.4)
    public bool ShareRoutesWithCar { get; init; }
}
