using System.Reflection.Metadata;
using Microsoft.CodeAnalysis;

namespace Sia.Examples.Notebook;

public sealed class MetadataReferenceProvider : ICompilationReferenceResolver
{
    private static readonly string[] s_CoreAssemblyNames =
        ["System.Private.CoreLib", "System.Runtime", "System.Console", "netstandard", "mscorlib"];

    private readonly AssemblyLoader _assemblies;
    private readonly PackageReferenceLoader _packages;
    private readonly HashSet<string> _hostAssemblyNames =
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
    internal int Version { get; private set; }

    // Shader intrinsics must use the host's exact Core image, not a separately downloaded package.
    internal async ValueTask<ReadOnlyMemory<byte>> EnsureHostReferenceAsync(
        string name, CancellationToken cancellationToken = default)
    {
        if (_packageReferences.ContainsKey(name)) {
            throw new InvalidOperationException($"Shader reference '{name}' must use the host version, not a separately loaded package.");
        }
        await _assemblies.LoadAsync(name, cancellationToken);
        var image = await _assemblies.GetImageAsync(name, cancellationToken);
        if (_hostAssemblyNames.Add(name)) Version++;
        return image;
    }

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
        wanted.UnionWith(_hostAssemblyNames);
        wanted.UnionWith(_packageReferences.Keys);
        await LoadMissingAsync(wanted, cancellationToken);
        return BuildReferences(wanted);
    }

    public async ValueTask<IReadOnlyList<PackageStatus>> SetPackagesAsync(
        IReadOnlyList<PackageRef> packages,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fetches = await Task.WhenAll(
            packages.Select(package => FetchAsync(package, cancellationToken).AsTask()));

        cancellationToken.ThrowIfCancellationRequested();
        _packageReferences.Clear();
        Version++;
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
            var fetched = await _packages.LoadReferencesAsync(
                package.Id,
                package.Version,
                cancellationToken);
            List<FetchedAssembly> assemblies = [];
            List<string> hostNames = [];
            foreach (var assembly in fetched.Assemblies) {
                if (_assemblies.KnownAssemblyNames.Contains(assembly.Name)) {
                    var host = await _assemblies.GetImageAsync(assembly.Name, cancellationToken);
                    if (!host.Span.SequenceEqual(assembly.Image))
                        throw new InvalidOperationException($"Package '{package.Id}' would replace host assembly '{assembly.Name}'. Use the host version.");
                    hostNames.Add(assembly.Name);
                }
                else assemblies.Add(assembly with { Analyzer = package.Analyzer || assembly.Analyzer });
            }
            return new(package with { Version = fetched.Version }, assemblies, hostNames, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            throw;
        }
        catch (Exception error) {
            return new(package, [], [], error);
        }
    }

    private PackageStatus Commit(PackageFetch fetch)
    {
        if (fetch.Error is { } error) {
            return new(fetch.Package, PackageLoadState.Failed, error.Message);
        }
        try {
            var prepared = fetch.Assemblies.Select(static assembly => (
                Assembly: assembly, Reference: MetadataReference.CreateFromImage(assembly.Image, filePath: assembly.Name))).ToArray();
            foreach (var entry in prepared) {
                var reader = ((AssemblyMetadata)entry.Reference.GetMetadata()).GetModules()[0].GetMetadataReader();
                var name = reader.GetString(reader.GetAssemblyDefinition().Name);
                if (!string.Equals(name, entry.Assembly.Name, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Package assembly '{entry.Assembly.Name}' contains '{name}' instead.");
            }
            DynamicAssemblyRegistry.RegisterImages(fetch.Assemblies);
            foreach (var entry in prepared.Where(static entry => !entry.Assembly.Analyzer)) {
                _packageReferences[entry.Assembly.Name] = entry.Reference;
            }
            _hostAssemblyNames.UnionWith(fetch.HostNames);
            Version++;
            return new(fetch.Package, PackageLoadState.Loaded, null);
        }
        catch (Exception failure) {
            return new(fetch.Package, PackageLoadState.Failed, failure.Message);
        }
    }

    private HashSet<string> ResolveWanted(string source)
    {
        var names = AssemblyReferenceResolver.ResolveAssemblyNames(
            AssemblyReferenceResolver.ResolveNamespaces(source),
            _assemblies.KnownAssemblyNames);

        var wanted = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
        foreach (var coreAssemblyName in s_CoreAssemblyNames) {
            if (_assemblies.KnownAssemblyNames.Contains(coreAssemblyName)) {
                wanted.Add(coreAssemblyName);
            }
        }
        wanted.UnionWith(_hostAssemblyNames);
        wanted.UnionWith(_packageReferences.Keys);
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
            }
            else if (_assemblies.TryGetLoaded(name, out var frameworkReference)) {
                result.Add(frameworkReference);
            }
        }
        return result;
    }

    private readonly record struct PackageFetch(
        PackageRef Package,
        IReadOnlyList<FetchedAssembly> Assemblies,
        IReadOnlyList<string> HostNames,
        Exception? Error);
}
