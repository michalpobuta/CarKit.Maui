using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace CarKit.Maui.SourceGenerators;

/// <summary>
/// Generates <c>CarKitGeneratedAppService</c> (the Android Auto car app service with its intent filter) and the
/// assembly-level manifest entries for the categories the app declares in <c>&lt;CarKitCategories&gt;</c>.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class CarAppServiceGenerator : IIncrementalGenerator
{
    private static readonly string _version = typeof(CarAppServiceGenerator).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var options = context.AnalyzerConfigOptionsProvider.Select(static (provider, _) => CarAppServiceOptions.From(provider.GlobalOptions));
        context.RegisterSourceOutput(options, static (output, value) =>
        {
            if (value.Render(_version) is { } source)
            {
                output.AddSource("CarKitGeneratedAppService.g.cs", SourceText.From(source, Encoding.UTF8));
            }
        });
    }
}
