#if !BROWSER
using Microsoft.CodeAnalysis;

namespace Sia_Examples.Notebook;

public sealed class MetadataReferenceProvider : IMetadataReferenceProvider
{
    private static readonly Lazy<IReadOnlyList<MetadataReference>> Cached = new(Resolve);

    public ValueTask<IReadOnlyList<MetadataReference>> GetReferencesAsync()
        => ValueTask.FromResult(Cached.Value);

    private static IReadOnlyList<MetadataReference> Resolve()
    {
        var tpa = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (string.IsNullOrEmpty(tpa)) {
            throw new InvalidOperationException(
                "TRUSTED_PLATFORM_ASSEMBLIES is unavailable; runtime compilation requires a framework-dependent (non-AOT, non-single-file) build.");
        }

        List<MetadataReference> references = [];
        foreach (var path in tpa.Split(Path.PathSeparator)) {
            var fileName = Path.GetFileNameWithoutExtension(path);
            if (string.IsNullOrEmpty(fileName)
                || string.Equals(fileName, "Sia.CodeGenerators", StringComparison.Ordinal)) {
                continue;
            }
            try {
                references.Add(MetadataReference.CreateFromFile(path));
            }
            catch (IOException) {
            }
        }
        return references;
    }
}
#endif
