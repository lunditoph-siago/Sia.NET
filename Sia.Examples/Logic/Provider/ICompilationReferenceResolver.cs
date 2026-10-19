using Microsoft.CodeAnalysis;

namespace Sia.Examples.Notebook;

public interface ICompilationReferenceResolver
{
    public ValueTask<IReadOnlyList<MetadataReference>> GetReferencesAsync(
        string source,
        CancellationToken cancellationToken = default);

    public ValueTask<IReadOnlyList<MetadataReference>> GetAllReferencesAsync(
        CancellationToken cancellationToken = default);
}
