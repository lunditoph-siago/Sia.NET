namespace Sia.Reactors;

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

public abstract class AggregatorBase<TId> : ReactorBase<TypeUnion<Sid<TId>>>
    where TId : notnull, IEquatable<TId>
{
    [AllowNull]
    private QuerySubscription _aggregationSubscription;
    private readonly Dictionary<TId, Entity> _aggrs = [];
    private readonly Stack<HashSet<Entity>> _groupPool = new();

    public Aggregation<TId> this[in TId component] => _aggrs[component].Get<Aggregation<TId>>();

    public override void OnInitialize(World world)
    {
        base.OnInitialize(world);

        _aggregationSubscription = new QuerySubscription(
            world.Query<TypeUnion<Aggregation<TId>>>(),
            OnAggregationCreated, OnAggregationReleased);

        Listen((Entity entity, in WorldEvents.Add<Aggregation<TId>> e) => {
            ref var aggr = ref entity.Get<Aggregation<TId>>();
            if (aggr.Aggregator != this) {
                return;
            }
            aggr._group ??= _groupPool.TryPop(out var pooled) ? pooled : [];
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

        _aggregationSubscription.Dispose();
        _aggregationSubscription = null;
    }

    private void OnAggregationCreated(Entity entity)
    {
        ref var aggr = ref entity.Get<Aggregation<TId>>();
        if (!_aggrs.TryAdd(aggr.Id, entity)) {
            return;
        }
        aggr.Aggregator = this;
        aggr._group ??= _groupPool.TryPop(out var pooled) ? pooled : [];
    }

    private void OnAggregationReleased(Entity entity)
    {
        ref var aggr = ref entity.Get<Aggregation<TId>>();
        if (!_aggrs.Remove(aggr.Id, out var removedEntity)) {
            return;
        }
        if (removedEntity != entity)  {
            _aggrs.Add(aggr.Id, removedEntity);
        }
    }

    public bool TryGet(in TId id, [MaybeNullWhen(false)] out Entity aggrEntity)
        => _aggrs.TryGetValue(id, out aggrEntity);

    private bool OnEntityIdChanged(Entity entity, in Sid<TId>.SetValue e)
    {
        ref var id = ref entity.Get<Sid<TId>>();
        RemoveFromAggregation(entity, id.Previous!);
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

    protected abstract Entity CreateAggregationEntity(in TId id);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AddToAggregation(Entity entity, in TId id)
    {
        if (!_aggrs.TryGetValue(id, out var aggrEntity)) {
            aggrEntity = CreateAggregationEntity(id);

            ref var aggr = ref aggrEntity.Get<Aggregation<TId>>();
            aggr.First = entity;
            aggr._group ??= _groupPool.TryPop(out var pooled) ? pooled : [];
            aggr._group.Add(entity);
        }
        else {
            aggrEntity.Get<Aggregation<TId>>()._group!.Add(entity);
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
            var groupEntity = aggrEntity;
            _aggrs.Remove(id);
            _groupPool.Push(group);
            aggr._group = null;
            ReleaseAggregation(groupEntity);
        }
        else if (aggr.First == entity) {
            aggr.First = group.First();
        }
    }

    private void ReleaseAggregation(Entity entity)
        => World.Dispatcher.RunAfterSend(() => {
            if (entity.IsValid) {
                entity.Destroy();
            }
        });
}

public class Aggregator<TId> : AggregatorBase<TId>
    where TId : notnull, IEquatable<TId>
{
    protected override Entity CreateAggregationEntity(in TId id)
        => World.Create(HList.From(new Aggregation<TId> { Id = id }));
}