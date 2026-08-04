using Microsoft.CodeAnalysis;

namespace Sia_Examples.Notebook;

public interface IMetadataReferenceProvider
{
    ValueTask<IReadOnlyList<MetadataReference>> GetReferencesAsync();
}
