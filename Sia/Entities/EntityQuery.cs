namespace Sia;

public sealed class EntityQuery : IEntityQuery
{
    public IReadOnlyList<IEntityHost> Hosts => _hosts;
    private readonly List<IEntityHost> _hosts;

    public EntityQuery()
    {
        _hosts = [];
    }

    public EntityQuery(IEnumerable<IEntityHost> hosts)
    {
        _hosts = new(hosts);
    }

    public void Add(IEntityHost host) => _hosts.Add(host);
    public void Remove(IEntityHost host) => _hosts.Remove(host);

    public void Dispose() { }
}