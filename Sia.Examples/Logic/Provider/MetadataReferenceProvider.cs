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

    public async ValueTask EnsurePackagesAsync(
        IReadOnlyList<PackageRef> packages,
        CancellationToken cancellationToken = default)
    {
        foreach (var package in packages) {
            cancellationToken.ThrowIfCancellationRequested();
            if (package.Source == PackageSource.Framework) {
                if (!_assemblies.KnownAssemblyNames.Contains(package.Id)) {
                    throw new InvalidOperationException(
                        $"Framework assembly '{package.Id}' is not available.");
                }
                _declaredAssemblyNames.Add(package.Id);
                await _assemblies.LoadAsync(package.Id, cancellationToken);
                continue;
            }

            var fetchedAssemblies = await _packages.LoadReferencesAsync(
                package.Id,
                package.Version,
                cancellationToken);
            foreach (var assembly in fetchedAssemblies) {
                _packageReferences[assembly.Name] = MetadataReference.CreateFromImage(
                    assembly.Image,
                    filePath: assembly.Name);
                DynamicAssemblyRegistry.Register(assembly.Name, assembly.Image);
                _declaredAssemblyNames.Add(assembly.Name);
            }
        }
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
        foreach (var name in wanted) {
            if (!_packageReferences.ContainsKey(name)
                && !_assemblies.TryGetLoaded(name, out _)) {
                await _assemblies.LoadAsync(name, cancellationToken);
            }
        }
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
}
