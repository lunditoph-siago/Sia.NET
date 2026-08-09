using System.Collections.Immutable;

namespace Sia_Examples.Notebook;

internal sealed class PackageRegistry
{
    private ImmutableArray<PackageStatus> _snapshot = [];

    public ImmutableArray<PackageStatus> Snapshot => _snapshot;

    public bool Declare(PackageRef package)
    {
        if (IndexOf(package) >= 0) {
            return false;
        }
        _snapshot = _snapshot.Add(new(package, PackageLoadState.Loading, null));
        return true;
    }

    public bool Resolve(PackageStatus status)
    {
        var index = IndexOf(status.Package);
        if (index >= 0 && _snapshot[index] == status) {
            return false;
        }
        _snapshot = index >= 0
            ? _snapshot.SetItem(index, status)
            : _snapshot.Add(status);
        return true;
    }

    private int IndexOf(PackageRef package)
    {
        for (var index = 0; index < _snapshot.Length; index++) {
            if (_snapshot[index].Package == package) {
                return index;
            }
        }
        return -1;
    }
}
