namespace Sia;

public partial class World
{
    public EntityRef this[Identity id] => _indexedEntities[id];

    private readonly Dictionary<Identity, EntityRef> _indexedEntities = [];
    private readonly HashSet<IReactiveEntityHost> _indexedHosts = [];
    private readonly HashSet<IReactiveEntityQuery> _indexerQueries = [];

    public void IndexHosts(IEntityMatcher matcher)
    {
        var query = Query(matcher);
        if (!_indexerQueries.Add(query)) {
            return;
        }

        query.OnEntityHostAdded += IndexHost;
        query.OnEntityHostRemoved += UnindexHost;

        foreach (var host in query.Hosts) {
            IndexHost(host);
        }
    }

    public void UnindexHosts(IEntityMatcher matcher)
    {
        var query = Query(matcher);
        if (!_indexerQueries.Remove(query)) {
            return;
        }

        query.OnEntityHostAdded -= IndexHost;
        query.OnEntityHostRemoved -= UnindexHost;

        foreach (var host in query.Hosts) {
            UnindexHost(host);
        }
    }

    private void IndexHost(IReactiveEntityHost host)
    {
        if (!_indexedHosts.Add(host)) {
            return;
        }

        host.OnEntityCreated += OnIndexedEntityAdded;
        host.OnEntityReleased += OnIndexedEntityReleased;
        host.OnDisposed += OnIndexedHostRemoved;

        foreach (var entity in host) {
            _indexedEntities.Add(entity.Id, entity);
        }
    }

    private void UnindexHost(IReactiveEntityHost host)
    {
        if (!_indexedHosts.Remove(host)) {
            return;
        }

        host.OnEntityCreated -= OnIndexedEntityAdded;
        host.OnEntityReleased -= OnIndexedEntityReleased;
        host.OnDisposed -= OnIndexedHostRemoved;

        OnIndexedHostRemoved(host);
    }

    private void OnIndexedEntityAdded(in EntityRef entity)
        => _indexedEntities.Add(entity.Id, entity);

    private void OnIndexedEntityReleased(in EntityRef entity)
        => _indexedEntities.Remove(entity.Id);
    
    private void OnIndexedHostRemoved(IEntityHost host)
    {
        host.ForSlice(_indexedEntities, (in Dictionary<Identity, EntityRef> es, ref Identity id) => {
            es.Remove(id);
        });
    }
}