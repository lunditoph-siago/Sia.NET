namespace Sia.Reactors;

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

public abstract class AggregatorBase<TId> : ReactorBase<TypeUnion<Sid<TId>>>
    where TId : notnull, IEquatable<TId>
{
    [AllowNull]
    private IReactiveEntityQuery _aggregationQuery;
    private readonly Dictionary<TId, Entity> _aggrs = [];
    private readonly Stack<HashSet<Entity>> _groupPool = new();

    public Aggregation<TId> this[in TId component] => _aggrs[component].Get<Aggregation<TId>>();

    public override void OnInitialize(World world)
    {
        base.OnInitialize(world);

        _aggregationQuery = world.Query<TypeUnion<Aggregation<TId>>>();
        _aggregationQuery.OnEntityHostAdded += host => {
            host.OnEntityCreated += OnAggregationCreated;
            host.OnEntityReleased += OnAggregationReleased;
        };
        
        Listen((Entity entity, in WorldEvents.Add<Aggregation<TId>> e) => {
            ref var aggr = ref entity.Get<Aggregation<TId>>();
            if (aggr.Aggregator != this) {
                return;
            }
            var group = _groupPool.TryPop(out var pooled) ? pooled : [];
            aggr._group = group;
        });

        Listen((Entity entity, in WorldEvents.Remove<Aggregation<TId>> e) => {
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

    private void OnAggregationCreated(Entity entity)
    {
        ref var aggr = ref entity.Get<Aggregation<TId>>();
        if (_aggrs.TryAdd(aggr.Id, entity)) {
            aggr.Aggregator = this;
        }
    }

    private void OnAggregationReleased(Entity entity)
    {
        ref var aggr = ref entity.Get<Aggregation<TId>>();
        _aggrs.Remove(aggr.Id, out var removedEntity);
        if (removedEntity != entity)  {
            _aggrs.Add(aggr.Id, removedEntity!);
        }
    }

    public bool TryGet(in TId id, [MaybeNullWhen(false)] out Entity aggrEntity)
        => _aggrs.TryGetValue(id, out aggrEntity);

    private bool OnEntityIdChanged(Entity entity, in Sid<TId>.SetValue e)
    {
        ref var id = ref entity.Get<Sid<TId>>();
        RemoveFromAggregation(entity, id.Previous);
        AddToAggregation(entity, id.Value);
        return false;
    }
    
    protected override void OnEntityAdded(Entity entity)
    {
        var id = entity.Get<Sid<TId>>().Value;
        AddToAggregation(entity, id);
    }

    protected override void OnEntityRemoved(Entity entity)
    {
        var id = entity.Get<Sid<TId>>().Value;
        RemoveFromAggregation(entity, id);
    }

    protected abstract Entity CreateAggregationEntity();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AddToAggregation(Entity entity, in TId id)
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
            aggrEntity!.Get<Aggregation<TId>>()._group!.Add(entity);
        }

        World.Send(aggrEntity, new Aggregation<TId>.EntityAdded(entity));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void RemoveFromAggregation(Entity entity, in TId id)
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
    protected override Entity CreateAggregationEntity()
        => World.Create(HList.From(new Aggregation<TId>()));
}