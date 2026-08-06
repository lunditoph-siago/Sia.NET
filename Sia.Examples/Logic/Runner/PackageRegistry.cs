using System.Collections.Immutable;

namespace Sia_Examples.Notebook;

public sealed class PackageRegistry
{
    private ImmutableArray<PackageStatus> _snapshot = [];

    public ImmutableArray<PackageStatus> Snapshot => _snapshot;

    public void Declare(PackageRef package)
        => ImmutableInterlocked.Update(ref _snapshot, static (current, pkg) =>
            IndexOf(current, pkg) >= 0
                ? current
                : current.Add(new PackageStatus(pkg, PackageLoadState.Loading, null)),
            package);

    public void Resolve(PackageRef package, PackageLoadState state, string? error)
        => ImmutableInterlocked.Update(ref _snapshot, static (current, args) => {
            var (pkg, resolvedState, resolvedError) = args;
            var index = IndexOf(current, pkg);
            var resolved = new PackageStatus(pkg, resolvedState, resolvedError);
            return index >= 0 ? current.SetItem(index, resolved) : current.Add(resolved);
        }, (package, state, error));

    private static int IndexOf(ImmutableArray<PackageStatus> snapshot, PackageRef package)
    {
        for (var i = 0; i < snapshot.Length; i++) {
            if (snapshot[i].Package == package) return i;
        }
        return -1;
    }
}
