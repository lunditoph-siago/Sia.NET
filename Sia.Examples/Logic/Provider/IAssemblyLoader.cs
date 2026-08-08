using Microsoft.CodeAnalysis;

namespace Sia_Examples.Notebook;

public interface IAssemblyLoader
{
    public IReadOnlySet<string> KnownAssemblyNames { get; }
    public Task<MetadataReference> LoadAsync(string name, CancellationToken ct = default);
}
