using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Sia_Examples.Notebook;

internal static class GeneratorPipeline
{
    private static int _discoveredVersion = -1;
    private static ImmutableArray<ISourceGenerator> _generators = [];

    public static Compilation Run(
        CSharpCompilation compilation,
        CSharpParseOptions parseOptions,
        out ImmutableArray<Diagnostic> diagnostics)
    {
        var version = DynamicAssemblyRegistry.Version;
        if (Volatile.Read(ref _discoveredVersion) != version) {
            _generators = Discover();
            _discoveredVersion = version;
        }

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            _generators,
            parseOptions: parseOptions);
        driver.RunGeneratorsAndUpdateCompilation(
            compilation, out var updated, out diagnostics);
        return updated;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "Generator assemblies are discovered dynamically from the app's own code generators and from registered NuGet packages; their generator types are never trimmed away.")]
    [UnconditionalSuppressMessage("Trimming", "IL2072",
        Justification = "Discovered generator types are always concrete IIncrementalGenerator implementations with a public parameterless constructor.")]
    private static ImmutableArray<ISourceGenerator> Discover()
    {
        var generatorType = typeof(IIncrementalGenerator);

        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "Sia.CodeGenerators",
        };
        candidates.UnionWith(DynamicAssemblyRegistry.AnalyzerNames);

        var builder = ImmutableArray.CreateBuilder<ISourceGenerator>();
        foreach (var name in candidates) {
            Assembly assembly;
            try {
                assembly = Assembly.Load(new AssemblyName(name));
            }
            catch (FileNotFoundException) {
                // A declared analyzer failed to load; its package will report
                // the failure in the packages panel, so skip it here.
                continue;
            }
            foreach (var type in assembly.GetTypes()) {
                if (type.IsAbstract || type.IsInterface || !generatorType.IsAssignableFrom(type)) {
                    continue;
                }
                var instance = (IIncrementalGenerator)Activator.CreateInstance(type)!;
                builder.Add(instance.AsSourceGenerator());
            }
        }
        return builder.ToImmutable();
    }
}
