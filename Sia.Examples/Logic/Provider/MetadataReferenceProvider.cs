using Microsoft.CodeAnalysis;

namespace Sia_Examples.Notebook;

public sealed class MetadataReferenceProvider : ICompilationReferenceResolver
{
    private static readonly string[] _coreAssemblyNames =
        ["System.Private.CoreLib", "System.Runtime", "System.Console", "netstandard", "mscorlib"];

    private readonly AssemblyLoader _assemblies;
    private readonly PackageReferenceLoader _packages;
    private readonly HashSet<string> _declaredAssemblyNames =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MetadataReference> _packageReferences =
        new(StringComparer.OrdinalIgnoreCase);

    public MetadataReferenceProvider(
        AssemblyLoader assemblies,
        PackageReferenceLoader packages)
    {
        _assemblies = assemblies;
        _packages = packages;
        AvailableFrameworkAssemblyNames = _assemblies.KnownAssemblyNames
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<string> AvailableFrameworkAssemblyNames { get; }

    public async ValueTask<IReadOnlyList<MetadataReference>> GetReferencesAsync(
        string source,
        CancellationToken cancellationToken = default)
    {
        var wanted = ResolveWanted(source);
        await LoadMissingAsync(wanted, cancellationToken);
        return BuildReferences(wanted);
    }

    public async ValueTask<IReadOnlyList<MetadataReference>> GetAllReferencesAsync(
        CancellationToken cancellationToken = default)
    {
        var wanted = new HashSet<string>(_assemblies.KnownAssemblyNames, StringComparer.OrdinalIgnoreCase);
        wanted.UnionWith(_declaredAssemblyNames);
        await LoadMissingAsync(wanted, cancellationToken);
        return BuildReferences(wanted);
    }

    public async ValueTask<IReadOnlyList<PackageStatus>> EnsurePackagesAsync(
        IReadOnlyList<PackageRef> packages,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fetches = await Task.WhenAll(
            packages.Select(package => FetchAsync(package, cancellationToken).AsTask()));

        var statuses = new PackageStatus[fetches.Length];
        for (var index = 0; index < fetches.Length; index++) {
            statuses[index] = Commit(fetches[index]);
        }
        return statuses;
    }

    private async ValueTask<PackageFetch> FetchAsync(
        PackageRef package,
        CancellationToken cancellationToken)
    {
        try {
            if (package.Source == PackageSource.Framework) {
                if (!_assemblies.KnownAssemblyNames.Contains(package.Id)) {
                    throw new InvalidOperationException(
                        $"Framework assembly '{package.Id}' is not available.");
                }
                await _assemblies.LoadAsync(package.Id, cancellationToken);
                return new(package, [], null);
            }

            var fetchedAssemblies = await _packages.LoadReferencesAsync(
                package.Id,
                package.Version,
                cancellationToken);
            return new(package, fetchedAssemblies, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            throw;
        }
        catch (Exception error) {
            return new(package, [], error);
        }
    }

    private PackageStatus Commit(PackageFetch fetch)
    {
        if (fetch.Error is { } error) {
            return new(fetch.Package, PackageLoadState.Failed, error.Message);
        }
        if (fetch.Package.Source == PackageSource.Framework) {
            _declaredAssemblyNames.Add(fetch.Package.Id);
        } else {
            foreach (var assembly in fetch.Assemblies) {
                _packageReferences[assembly.Name] = MetadataReference.CreateFromImage(
                    assembly.Image,
                    filePath: assembly.Name);
                DynamicAssemblyRegistry.Register(assembly.Name, assembly.Image);
                _declaredAssemblyNames.Add(assembly.Name);
            }
        }
        return new(fetch.Package, PackageLoadState.Loaded, null);
    }

    private HashSet<string> ResolveWanted(string source)
    {
        var names = AssemblyReferenceResolver.ResolveAssemblyNames(
            AssemblyReferenceResolver.ResolveNamespaces(source),
            _assemblies.KnownAssemblyNames);

        var wanted = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
        foreach (var coreAssemblyName in _coreAssemblyNames) {
            if (_assemblies.KnownAssemblyNames.Contains(coreAssemblyName)) {
                wanted.Add(coreAssemblyName);
            }
        }
        wanted.UnionWith(_declaredAssemblyNames);
        return wanted;
    }

    private async ValueTask LoadMissingAsync(
        HashSet<string> wanted,
        CancellationToken cancellationToken)
    {
        var missing = wanted.Where(name =>
            !_packageReferences.ContainsKey(name) && !_assemblies.TryGetLoaded(name, out _));
        await Task.WhenAll(missing.Select(name => _assemblies.LoadAsync(name, cancellationToken).AsTask()));
    }

    private IReadOnlyList<MetadataReference> BuildReferences(HashSet<string> wanted)
    {
        List<MetadataReference> result = [];
        foreach (var name in wanted) {
            if (_packageReferences.TryGetValue(name, out var packageReference)) {
                result.Add(packageReference);
            } else if (_assemblies.TryGetLoaded(name, out var frameworkReference)) {
                result.Add(frameworkReference);
            }
        }
        return result;
    }

    private readonly record struct PackageFetch(
        PackageRef Package,
        IReadOnlyList<FetchedAssembly> Assemblies,
        Exception? Error);
}
