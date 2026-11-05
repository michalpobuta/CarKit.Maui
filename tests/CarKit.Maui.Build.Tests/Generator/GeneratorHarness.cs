using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using CarKit.Maui.SourceGenerators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CarKit.Maui.Build.Tests.Generator;

// Runs the generator on a compilation with stubs of the Android attributes and the CarKit base class
internal static class GeneratorHarness
{
    private const string Stubs = """
        namespace Android.App
        {
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class ServiceAttribute : System.Attribute
            {
                public string? Name { get; set; }
                public bool Exported { get; set; }
                public Android.Content.PM.ForegroundService ForegroundServiceType { get; set; }
            }

            [System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = true)]
            public sealed class IntentFilterAttribute(string[] actions) : System.Attribute
            {
                public string[] Actions { get; } = actions;
                public string[]? Categories { get; set; }
            }

            [System.AttributeUsage(System.AttributeTargets.Assembly | System.AttributeTargets.Class, AllowMultiple = true)]
            public sealed class MetaDataAttribute(string name) : System.Attribute
            {
                public string Name { get; } = name;
                public string? Value { get; set; }
                public string? Resource { get; set; }
            }

            [System.AttributeUsage(System.AttributeTargets.Assembly, AllowMultiple = true)]
            public sealed class UsesPermissionAttribute(string name) : System.Attribute
            {
                public string Name { get; } = name;
            }
        }

        namespace Android.Content
        {
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class BroadcastReceiverAttribute : System.Attribute
            {
                public string? Name { get; set; }
                public bool Exported { get; set; }
            }
        }

        namespace Android.Content.PM
        {
            [System.Flags]
            public enum ForegroundService
            {
                TypeNone = 0,
                TypeMediaPlayback = 2,
            }
        }

        namespace CarKit.Maui
        {
            public abstract class CarKitAppServiceBase
            {
            }
        }

        namespace CarKit.Maui.Media
        {
            public abstract class CarKitMediaLibraryService
            {
            }
        }

        namespace CarKit.Maui.Communication
        {
            public abstract class CarKitMessageActionReceiver
            {
            }
        }
        """;

    private static readonly ImmutableArray<MetadataReference> _references =
        [.. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator).Select(p => MetadataReference.CreateFromFile(p))];

    public static GeneratorRun Run(IReadOnlyDictionary<string, string> buildProperties)
    {
        var compilation = CSharpCompilation.Create(
            "App",
            [CSharpSyntaxTree.ParseText(Stubs, new CSharpParseOptions(LanguageVersion.Latest))],
            _references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new CarAppServiceGenerator().AsSourceGenerator()],
            parseOptions: new CSharpParseOptions(LanguageVersion.Latest),
            optionsProvider: new OptionsProvider(buildProperties.ToDictionary(p => "build_property." + p.Key, p => p.Value)));
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);
        var result = driver.GetRunResult();
        return new GeneratorRun(
            [.. result.Results.SelectMany(r => r.GeneratedSources)],
            [.. generatorDiagnostics, .. output.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning)]);
    }

    private sealed class OptionsProvider(Dictionary<string, string> values) : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions { get; } = new Options(values);

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => Options.Empty;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => Options.Empty;
    }

    private sealed class Options(Dictionary<string, string> values) : AnalyzerConfigOptions
    {
        public static readonly Options Empty = new([]);

        public override bool TryGetValue(string key, [NotNullWhen(true)] out string? value) => values.TryGetValue(key, out value);
    }
}

internal sealed record GeneratorRun(ImmutableArray<GeneratedSourceResult> Sources, ImmutableArray<Diagnostic> Diagnostics);
