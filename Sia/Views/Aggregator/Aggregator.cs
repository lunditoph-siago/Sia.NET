namespace Sia;

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

public class Aggregator<TAggregationEntity, TId> : ViewBase<TypeUnion<Sid<TId>>>
    where TAggregationEntity : IAggregationEntity<TId>
    where TId : IEquatable<TId>
{
    [AllowNull]
    private World.EntityQuery _aggregationQuery;
    private readonly Dictionary<TId, Aggregation<TId>> _aggrs = new();
    private readonly Stack<HashSet<EntityRef>> _groupPool = new();

    public Aggregation<TId> this[in TId component] => _aggrs[component];

    public Aggregator()
    {
        EntityUtility.CheckComponent<TAggregationEntity, Aggregation<TId>>();
    }

    public override void OnInitialize(World world)
    {
        _aggregationQuery = world.Query<TypeUnion<Aggregation<TId>>>();
        _aggregationQuery.OnEntityHostAdded += host => host.OnEntityReleased += OnAggregationReleased;

        world.Dispatcher.Listen<Sid<TId>.SetValue>(OnEntityIdChanged);
        base.OnInitialize(world);
    }

    public override void OnUninitialize(World world)
    {
        base.OnUninitialize(world);

        foreach (var host in _aggregationQuery.Hosts) {
            host.OnEntityReleased -= OnAggregationReleased;
        }

        _aggregationQuery.Dispose();
        _aggregationQuery = null;

        world.Dispatcher.Unlisten<Sid<TId>.SetValue>(OnEntityIdChanged);
    }

    private void OnAggregationReleased(in EntityRef target)
    {
        ref var aggr = ref target.Get<Aggregation<TId>>();
        if (!_aggrs.Remove(aggr.Id)) {
            return;
        }
        var group = aggr.RawGroup;
        foreach (var entity in group) {
            entity.Dispose();
        }
        group.Clear();
        _groupPool.Push(group);
    }

    private bool OnEntityIdChanged(in EntityRef entity, in Sid<TId>.SetValue e)
    {
        ref var id = ref entity.Get<Sid<TId>>();
        RemoveFromAggregation(entity, id.Previous);
        AddToAggregation(entity, id.Value);
        return false;
    }

    public bool TryGet(in TId id, out Aggregation<TId> aggregation)
        => _aggrs.TryGetValue(id, out aggregation);
    
    protected override void OnEntityAdded(in EntityRef entity)
    {
        var id = entity.Get<Sid<TId>>().Value;
        AddToAggregation(entity, id);
    }

    protected override void OnEntityRemoved(in EntityRef entity)
    {
        var id = entity.Get<Sid<TId>>().Value;
        RemoveFromAggregation(entity, id);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AddToAggregation(in EntityRef entity, in TId id)
    {
        ref var aggr = ref CollectionsMarshal.GetValueRefOrAddDefault(_aggrs, id, out bool exists);

        if (!exists) {
            var aggrEntity = TAggregationEntity.Create(World);
            aggr = new(aggrEntity, id, _groupPool.TryPop(out var pooled) ? pooled : new());
            aggrEntity.Get<Aggregation<TId>>() = aggr;
        }

        aggr.RawGroup.Add(entity);
        World.Send(aggr.Entity, new Aggregation.EntityAdded(entity));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void RemoveFromAggregation(in EntityRef entity, in TId id)
    {
        ref var aggr = ref CollectionsMarshal.GetValueRefOrNullRef(_aggrs, id);
        if (Unsafe.IsNullRef(ref aggr)) {
            return;
        }

        var group = aggr.RawGroup;
        if (!group.Remove(entity)) {
            throw new InvalidOperationException("Internal: failed to remove entity from aggregation");
        }
        World.Send(aggr.Entity, new Aggregation.EntityRemoved(entity));

        if (group.Count == 0) {
            var aggrEntity = aggr.Entity;
            _aggrs.Remove(id);
            _groupPool.Push(group);
            aggrEntity.Dispose();
        }
    }
}