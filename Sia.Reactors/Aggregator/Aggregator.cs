namespace Sia.Reactors;

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

public abstract class AggregatorBase<TId> : ReactorBase<TypeUnion<Sid<TId>>>
    where TId : notnull, IEquatable<TId>
{
    [AllowNull]
    private IReactiveEntityQuery _aggregationQuery;
    private readonly Dictionary<TId, EntityRef> _aggrs = [];
    private readonly Stack<HashSet<EntityRef>> _groupPool = new();

    public Aggregation<TId> this[in TId component] => _aggrs[component].Get<Aggregation<TId>>();

    public override void OnInitialize(World world)
    {
        base.OnInitialize(world);

        _aggregationQuery = world.Query<TypeUnion<Aggregation<TId>>>();
        _aggregationQuery.OnEntityHostAdded += host => {
            host.OnEntityCreated += OnAggregationCreated;
            host.OnEntityReleased += OnAggregationReleased;
        };
        
        Listen((in EntityRef entity, in WorldEvents.Add<Aggregation<TId>> e) => {
            ref var aggr = ref entity.Get<Aggregation<TId>>();
            if (aggr.Aggregator != this) {
                return;
            }
            var group = _groupPool.TryPop(out var pooled) ? pooled : [];
            aggr._group = group;
        });

        Listen((in EntityRef entity, in WorldEvents.Remove<Aggregation<TId>> e) => {
            ref var aggr = ref entity.Get<Aggregation<TId>>();
            if (aggr.Aggregator != this) {
                return;
            }
            var group = aggr._group;
            if (group != null) {
                group.Clear();
                _groupPool.Push(group);
            }
            aggr.Aggregator = null;
        });

        Listen<Sid<TId>.SetValue>(OnEntityIdChanged);
    }

    public override void OnUninitialize(World world)
    {
        base.OnUninitialize(world);

        foreach (var host in _aggregationQuery.Hosts) {
            host.OnEntityCreated -= OnAggregationCreated;
            host.OnEntityReleased -= OnAggregationReleased;
        }

        _aggregationQuery.Dispose();
        _aggregationQuery = null;
    }

    private void OnAggregationCreated(in EntityRef entity)
    {
        ref var aggr = ref entity.Get<Aggregation<TId>>();
        if (_aggrs.TryAdd(aggr.Id, entity)) {
            aggr.Aggregator = this;
        }
    }

    private void OnAggregationReleased(in EntityRef entity)
    {
        ref var aggr = ref entity.Get<Aggregation<TId>>();
        _aggrs.Remove(aggr.Id, out var removedEntity);
        if (removedEntity != entity)  {
            _aggrs.Add(aggr.Id, removedEntity);
        }
    }

    public bool TryGet(in TId id, out EntityRef aggrEntity)
        => _aggrs.TryGetValue(id, out aggrEntity);

    public bool TryGet(in TId id, out EntityRef aggrEntity, out Aggregation<TId> aggr)
    {
        if (!_aggrs.TryGetValue(id, out aggrEntity)) {
            aggr = default;
            return false;
        }
        aggr = aggrEntity.Get<Aggregation<TId>>();
        return true;
    }

    private bool OnEntityIdChanged(in EntityRef entity, in Sid<TId>.SetValue e)
    {
        ref var id = ref entity.Get<Sid<TId>>();
        RemoveFromAggregation(entity, id.Previous);
        AddToAggregation(entity, id.Value);
        return false;
    }
    
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

    protected abstract EntityRef CreateAggregationEntity();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AddToAggregation(in EntityRef entity, in TId id)
    {
        ref var aggrEntity = ref CollectionsMarshal.GetValueRefOrAddDefault(_aggrs, id, out bool exists);

        if (!exists) {
            aggrEntity = CreateAggregationEntity();

            ref var aggr = ref aggrEntity.Get<Aggregation<TId>>();
            aggr.Id = id;
            aggr.First = entity;
            aggr._group = _groupPool.TryPop(out var pooled) ? pooled : [];
            aggr._group.Add(entity);
        }
        else {
            aggrEntity.Get<Aggregation<TId>>()._group!.Add(entity);
        }

        World.Send(aggrEntity, new Aggregation<TId>.EntityAdded(entity));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void RemoveFromAggregation(in EntityRef entity, in TId id)
    {
        ref var aggrEntity = ref CollectionsMarshal.GetValueRefOrNullRef(_aggrs, id);
        if (Unsafe.IsNullRef(ref aggrEntity)) {
            return;
        }

        ref var aggr = ref aggrEntity.Get<Aggregation<TId>>();
        var group = aggr._group!;

        if (!group.Remove(entity)) {
            return;
        }
        World.Send(aggrEntity, new Aggregation<TId>.EntityRemoved(entity));

        if (group.Count == 0) {
            _aggrs.Remove(id);
            _groupPool.Push(group);
            aggrEntity.Dispose();
        }
        else if (aggr.First == entity) {
            aggr.First = group.First();
        }
    }
}

public class Aggregator<TId> : AggregatorBase<TId>
    where TId : notnull, IEquatable<TId>
{
    protected override EntityRef CreateAggregationEntity()
        => World.CreateInArrayHost(HList.Create(new Aggregation<TId>()));
}