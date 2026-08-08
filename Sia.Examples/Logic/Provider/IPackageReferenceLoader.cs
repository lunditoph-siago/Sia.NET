namespace Sia_Examples.Notebook;

public interface IPackageReferenceLoader
{
    public Task<IReadOnlyList<FetchedAssembly>> LoadReferencesAsync(
        string packageId, string? version, CancellationToken ct = default);
}
