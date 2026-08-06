using Microsoft.CodeAnalysis;

namespace Sia_Examples.Notebook;

public interface IMetadataReferenceProvider
{
    public ValueTask<IReadOnlyList<MetadataReference>> GetReferencesAsync(string source);

    public ValueTask<IReadOnlyList<MetadataReference>> GetAllReferencesAsync();

    public Task EnsurePackagesAsync(IReadOnlyList<PackageRef> packages, CancellationToken cancellationToken = default);

    public IReadOnlyList<string> AvailableFrameworkAssemblyNames { get; }
}
