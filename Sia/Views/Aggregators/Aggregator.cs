namespace Sia;

using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

public static class Aggregation
{
    public readonly record struct EntityAdded(EntityRef Entity) : IEvent;
    public readonly record struct EntityRemoved(EntityRef Entity) : IEvent;
}

public readonly record struct Aggregation<TId> : IEnumerable<EntityRef>, IDisposable
    where TId : IEquatable<TId>
{
    public EntityRef Entity { get; }
    public TId Id { get; }
    public IReadOnlySet<EntityRef> Group => _group;

    internal readonly HashSet<EntityRef> _group;

    internal Aggregation(in EntityRef entity, in TId id, HashSet<EntityRef> group)
    {
        Entity = entity;
        Id = id;
        _group = group;
    }

    public HashSet<EntityRef>.Enumerator GetEnumerator()
        => _group.GetEnumerator();

    IEnumerator<EntityRef> IEnumerable<EntityRef>.GetEnumerator()
        => _group.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator()
        => _group.GetEnumerator();

    public void Dispose()
    {
        Entity.Dispose();
    }
}

public class Aggregator<TEntity, TId> : ViewBase<TypeUnion<Id<TId>>>
    where TEntity : IAggregationEntity<TId>
    where TId : IEquatable<TId>
{
    [AllowNull]
    private World.EntityQuery _aggregationQuery;
    private readonly Dictionary<TId, Aggregation<TId>> _aggrs = new();
    private readonly Stack<HashSet<EntityRef>> _groupPool = new();

    public Aggregation<TId> this[in TId component] {
        get => _aggrs[component];
    }

    public Aggregator()
    {
        if (!EntityDescriptor.Get<TEntity>().Contains<Aggregation<TId>>()) {
            throw new InvalidDataException(
                "Entity does not contain required component " + typeof(Aggregation<TId>));
        }
    }

    public override void OnInitialize(World world)
    {
        base.OnInitialize(world);

        _aggregationQuery = world.Query<TypeUnion<Aggregation<TId>>>();
        _aggregationQuery.OnEntityHostAdded += host => host.OnEntityReleased += OnAggregationReleased;

        world.Dispatcher.Listen<Id<TId>.SetValue>(OnEntityIdChanged);
    }

    public override void OnUninitialize(World world)
    {
        base.OnUninitialize(world);

        foreach (var host in _aggregationQuery.Hosts) {
            host.OnEntityReleased -= OnAggregationReleased;
        }

        _aggregationQuery.Dispose();
        _aggregationQuery = null;

        world.Dispatcher.Unlisten<Id<TId>.SetValue>(OnEntityIdChanged);
    }

    private bool OnEntityIdChanged(in EntityRef entity, in Id<TId>.SetValue e)
    {
        ref var id = ref entity.Get<Id<TId>>();
        RemoveFromAggregation(entity, id.Previous);
        AddToAggregation(entity, id.Value);
        return false;
    }

    private void OnAggregationReleased(in EntityRef target)
    {
        ref var aggr = ref target.Get<Aggregation<TId>>();
        if (!_aggrs.Remove(aggr.Id)) {
            return;
        }
        var group = aggr._group;
        foreach (var entity in group) {
            entity.Dispose();
        }
        group.Clear();
        _groupPool.Push(group);
    }

    public bool TryGet(in TId id, out Aggregation<TId> aggregation)
        => _aggrs.TryGetValue(id, out aggregation);
    
    protected override void OnEntityAdded(in EntityRef entity)
    {
        var id = entity.Get<Id<TId>>().Value;
        AddToAggregation(entity, id);
    }

    protected override void OnEntityRemoved(in EntityRef entity)
    {
        var id = entity.Get<Id<TId>>().Value;
        RemoveFromAggregation(entity, id);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AddToAggregation(in EntityRef entity, in TId id)
    {
        ref var aggr = ref CollectionsMarshal.GetValueRefOrAddDefault(_aggrs, id, out bool exists);

        if (!exists) {
            var aggrEntity = TEntity.Create(World);
            aggr = new(aggrEntity, id, _groupPool.TryPop(out var pooled) ? pooled : new());
            aggrEntity.Get<Aggregation<TId>>() = aggr;
        }

        aggr._group.Add(entity);
        World.Send(aggr.Entity, new Aggregation.EntityAdded(entity));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void RemoveFromAggregation(in EntityRef entity, in TId id)
    {
        ref var aggr = ref CollectionsMarshal.GetValueRefOrNullRef(_aggrs, id);
        if (Unsafe.IsNullRef(ref aggr)) {
            return;
        }

        var group = aggr._group;
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