using System.Collections.Immutable;

namespace Sia.Examples.Notebook;

internal sealed class PackageRegistry
{
    private ImmutableArray<PackageStatus> _snapshot = [];
    private readonly SemaphoreSlim _loading = new(1, 1);
    private int _revision;
    private int _resolvedRevision;
    private CancellationTokenSource? _revisionCancellation;

    public ImmutableArray<PackageStatus> Snapshot => _snapshot;
    public event Action? Changed;

    public bool Declare(PackageRef package)
    {
        package = Normalize(package);
        var index = IndexOf(package);
        if (index >= 0 && _snapshot[index].Package.Analyzer == package.Analyzer
            && string.Equals(_snapshot[index].Package.Version, package.Version, StringComparison.OrdinalIgnoreCase)) {
            return false;
        }
        _snapshot = index < 0 ? _snapshot.Add(new(package, PackageLoadState.Loading, null))
            : _snapshot.SetItem(index, new(package, PackageLoadState.Loading, null));
        Invalidate();
        return true;
    }

    public void Replace(IEnumerable<PackageRef> packages)
    {
        var declared = packages.Select(Normalize).ToArray();
        if (declared.Select(static package => package.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != declared.Length)
            throw new InvalidDataException("Declare each package ID only once.");
        if (_snapshot.Select(static status => status.Package).SequenceEqual(declared)) return;
        _snapshot = [..declared.Select(static package => new PackageStatus(package, PackageLoadState.Loading, null))];
        Invalidate();
    }

    public void Retry()
    {
        _snapshot = [.._snapshot.Select(static status => status with { State = PackageLoadState.Loading, Error = null })];
        Invalidate();
    }

    public async Task EnsureAsync(MetadataReferenceProvider references, CancellationToken cancellationToken = default)
    {
        await _loading.WaitAsync(cancellationToken);
        try {
            while (_resolvedRevision != _revision) {
                var revision = _revision;
                using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                _revisionCancellation = operation;
                IReadOnlyList<PackageStatus> statuses;
                try {
                    statuses = await references.SetPackagesAsync(
                        _snapshot.Select(static status => status.Package).ToArray(), operation.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && revision != _revision) { continue; }
                finally { _revisionCancellation = null; }
                cancellationToken.ThrowIfCancellationRequested();
                if (revision != _revision) continue;
                _snapshot = [..statuses];
                _resolvedRevision = revision;
                Changed?.Invoke();
            }
        }
        finally { _loading.Release(); }
    }

    private void Invalidate()
    {
        _revision++;
        _revisionCancellation?.Cancel();
        Changed?.Invoke();
    }

    private static PackageRef Normalize(PackageRef package)
    {
        var id = package.Id?.Trim() ?? "";
        var version = package.Version?.Trim();
        if (id.Length == 0 || id.Any(static character => !char.IsAsciiLetterOrDigit(character) && character is not ('.' or '-' or '_')))
            throw new InvalidDataException("A package ID may contain letters, digits, dots, hyphens and underscores.");
        if (version is { Length: > 0 } && version.Any(static character => !char.IsAsciiLetterOrDigit(character) && character is not ('.' or '-' or '+')))
            throw new InvalidDataException("Use an explicit package version, without ranges or wildcards.");
        return new(id, string.IsNullOrEmpty(version) ? null : version, package.Analyzer);
    }

    private int IndexOf(PackageRef package)
    {
        for (var index = 0; index < _snapshot.Length; index++) {
            if (string.Equals(_snapshot[index].Package.Id, package.Id, StringComparison.OrdinalIgnoreCase))
                return index;
        }
        return -1;
    }
}
