using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Sia_Examples.Notebook;

internal static class GeneratorPipeline
{
    private static readonly Lazy<ImmutableArray<ISourceGenerator>> _generators = new(Discover);

    public static Compilation Run(
        CSharpCompilation compilation,
        CSharpParseOptions parseOptions,
        out ImmutableArray<Diagnostic> diagnostics)
    {
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            _generators.Value,
            parseOptions: parseOptions);
        driver.RunGeneratorsAndUpdateCompilation(
            compilation, out var updated, out diagnostics);
        return updated;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "Sia.CodeGenerators is a fixed, always-referenced assembly; its generator types are never trimmed away.")]
    [UnconditionalSuppressMessage("Trimming", "IL2072",
        Justification = "Discovered generator types are always concrete IIncrementalGenerator implementations with a public parameterless constructor.")]
    private static ImmutableArray<ISourceGenerator> Discover()
    {
        var assembly = typeof(Sia.CodeGenerators.SiaReactiveGenerator).Assembly;
        var generatorType = typeof(IIncrementalGenerator);

        var builder = ImmutableArray.CreateBuilder<ISourceGenerator>();
        foreach (var type in assembly.GetTypes()) {
            if (type.IsAbstract || type.IsInterface || !generatorType.IsAssignableFrom(type)) {
                continue;
            }
            var instance = (IIncrementalGenerator)Activator.CreateInstance(type)!;
            builder.Add(instance.AsSourceGenerator());
        }
        return builder.ToImmutable();
    }
}
