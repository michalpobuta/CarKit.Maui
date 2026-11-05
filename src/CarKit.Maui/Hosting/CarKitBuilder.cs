using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace CarKit.Maui;

/// <summary>Configures CarKit inside <see cref="CarKitAppBuilderExtensions.UseCarKit"/>.</summary>
public sealed class CarKitBuilder
{
    internal CarKitBuilder(IServiceCollection services, CarAppCategory? projectCategories)
    {
        Services = services;
        ProjectCategories = projectCategories;
        Options.Categories = projectCategories ?? CarAppCategory.None;
    }

    /// <summary>Gets the app services, for module extensions such as <c>UseMedia</c>.</summary>
    public IServiceCollection Services { get; }

    /// <summary>
    /// Gets or sets the app categories. Defaults to <c>&lt;CarKitCategories&gt;</c> from the app project file, which
    /// also generates entitlements and manifest entries; a different value fails at startup.
    /// </summary>
    public CarAppCategory Categories
    {
        get => Options.Categories;
        set => Options.Categories = value;
    }

    /// <summary>Gets or sets how templates the platform does not support are handled.</summary>
    public UnsupportedTemplateBehavior UnsupportedTemplateBehavior
    {
        get => Options.UnsupportedTemplateBehavior;
        set => Options.UnsupportedTemplateBehavior = value;
    }

    /// <summary>Gets or sets the validation mode. Defaults to <see cref="CarValidationMode.Strict"/> in Debug and <see cref="CarValidationMode.Warn"/> in Release builds.</summary>
    public CarValidationMode Validation
    {
        get => Options.Validation;
        set => Options.Validation = value;
    }

    /// <summary>Gets the content limit options.</summary>
    public CarLimitOptions Limits => Options.Limits;

    /// <summary>Gets the diagnostics options.</summary>
    public CarDiagnosticsOptions Diagnostics => Options.Diagnostics;

    /// <summary>Gets the Android Auto and Android Automotive options.</summary>
    public CarKitAndroidOptions Android { get; } = new();

    internal CarKitOptions Options { get; } = new();

    internal CarAppCategory? ProjectCategories { get; }

    internal Action<IServiceCollection>? HandlerRegistration { get; private set; }

    // The module that drives the car screens instead of the app's handler (Stage 5: media.UseAutoBrowse())
    internal (string Source, Action<IServiceCollection> Registration)? ModuleHandler { get; private set; }

    private readonly HashSet<string> _modules = new(StringComparer.Ordinal);

    // Modules (UseMedia, …) register once; a second call would duplicate the platform services
    internal void AddModule(string name)
    {
        if (!_modules.Add(name))
        {
            throw new InvalidOperationException($"car.{name}(...) was already called; configure the module in one call.");
        }
    }

    internal void UseModuleHandler(string source, Action<IServiceCollection> registration) => ModuleHandler = (source, registration);

    /// <summary>Registers the app's <see cref="ICarAppHandler"/> as a singleton; a later call replaces an earlier one.</summary>
    /// <typeparam name="THandler">The handler type, created by dependency injection.</typeparam>
    /// <returns>This builder.</returns>
    public CarKitBuilder UseHandler<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] THandler>()
        where THandler : class, ICarAppHandler
    {
        HandlerRegistration = static services => services.AddSingleton<ICarAppHandler, THandler>();
        return this;
    }
}
