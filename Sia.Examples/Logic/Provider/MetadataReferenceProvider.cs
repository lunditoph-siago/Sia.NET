using Microsoft.CodeAnalysis;

namespace Sia_Examples.Notebook;

public sealed class MetadataReferenceProvider : IMetadataReferenceProvider
{
    private static readonly string[] AlwaysCore =
        ["System.Private.CoreLib", "System.Runtime", "System.Console", "netstandard", "mscorlib"];

    private readonly IAssemblyLoader _assemblies;
    private readonly IPackageReferenceLoader _packages;
    private readonly HashSet<string> _declared = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<MetadataReference> _packageRefs = [];

    public MetadataReferenceProvider(IAssemblyLoader assemblies, IPackageReferenceLoader packages)
    {
        _assemblies = assemblies;
        _packages = packages;
    }

    public IReadOnlyList<string> AvailableFrameworkAssemblyNames =>
        _assemblies.KnownAssemblyNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();

    public async ValueTask<IReadOnlyList<MetadataReference>> GetReferencesAsync(string source)
    {
        var wanted = ResolveWanted(source);
        await FetchMissingAsync(wanted);
        return BuildReferences(wanted);
    }

    public async ValueTask<IReadOnlyList<MetadataReference>> GetAllReferencesAsync()
    {
        var wanted = new HashSet<string>(_assemblies.KnownAssemblyNames, StringComparer.OrdinalIgnoreCase);
        wanted.UnionWith(_declared);
        await FetchMissingAsync(wanted);
        return BuildReferences(wanted);
    }

    public async Task EnsurePackagesAsync(IReadOnlyList<PackageRef> packages, CancellationToken ct = default)
    {
        foreach (var p in packages)
        {
            if (p.Source == PackageSource.Framework)
            {
                if (!_assemblies.KnownAssemblyNames.Contains(p.Id))
                    throw new InvalidOperationException(
                        $"Framework assembly '{p.Id}' not found in available assemblies.");
                _declared.Add(p.Id);
                continue;
            }

            var asm = await _packages.LoadReferencesAsync(p.Id, p.Version, ct);
            foreach (var a in asm)
            {
                _packageRefs.Add(MetadataReference.CreateFromImage(a.Image, filePath: a.Name));
                DynamicAssemblyRegistry.Register(a.Name, a.Image);
                _declared.Add(a.Name);
            }
        }
    }

    private HashSet<string> ResolveWanted(string source)
    {
        var names = AssemblyReferenceResolver.ResolveAssemblyNames(
            AssemblyReferenceResolver.ResolveNamespaces(source),
            _assemblies.KnownAssemblyNames);

        var wanted = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
        foreach (var c in AlwaysCore)
            if (_assemblies.KnownAssemblyNames.Contains(c)) wanted.Add(c);
        wanted.UnionWith(_declared);
        return wanted;
    }

    private async Task FetchMissingAsync(HashSet<string> wanted)
    {
        var missing = wanted.Where(n => !IsLoaded(n)).ToList();
        if (missing.Count == 0) return;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var sem = new SemaphoreSlim(4);
        var tasks = missing.Select(async n =>
        {
            await sem.WaitAsync(cts.Token);
            try { await _assemblies.LoadAsync(n, cts.Token); }
            catch (OperationCanceledException) { }
            finally { sem.Release(); }
        });

        await Task.WhenAll(tasks);
    }

    private bool IsLoaded(string name)
    {
        var task = _assemblies.LoadAsync(name);
        return task.IsCompletedSuccessfully;
    }

    private IReadOnlyList<MetadataReference> BuildReferences(HashSet<string> wanted)
    {
        List<MetadataReference> result = [];
        foreach (var n in wanted)
        {
            var task = _assemblies.LoadAsync(n);
            if (task.IsCompletedSuccessfully)
                result.Add(task.Result);
        }
        result.AddRange(_packageRefs);
        return result;
    }
}
