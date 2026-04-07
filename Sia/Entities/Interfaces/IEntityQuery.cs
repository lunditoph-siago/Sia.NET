namespace Sia;

public interface IEntityQuery : IDisposable
{
    int Count {
        get {
            var count = 0;
            for (var i = 0; i != Hosts.Count; ++i) {
                count += Hosts[i].Count;
            }
            return count;
        }
    }
    int Version { get; }

    IReadOnlyList<IEntityHost> Hosts { get; }
}

public interface IReactiveEntityQuery : IEntityQuery
{
    new IReadOnlyList<IReactiveEntityHost> Hosts { get; }

    public event Action<IReactiveEntityHost>? OnEntityHostAdded;
    public event Action<IReactiveEntityHost>? OnEntityHostRemoved;
}