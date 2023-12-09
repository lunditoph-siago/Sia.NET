namespace Sia;

using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

public class Mapper<TId> : ViewBase<TypeUnion<Sid<TId>>>, IReadOnlyDictionary<TId, EntityRef>
    where TId : notnull, IEquatable<TId>
{
    public IEnumerable<TId> Keys => _maps.Keys;
    public IEnumerable<EntityRef> Values => _maps.Values;

    public int Count => _maps.Count;

    public EntityRef this[TId key] => _maps[key];

    private readonly Dictionary<TId, EntityRef> _maps = [];

    public override void OnInitialize(World world)
    {
        world.Dispatcher.Listen<Sid<TId>.SetValue>(OnEntityIdChanged);
        base.OnInitialize(world);
    }

    public override void OnUninitialize(World world)
    {
        base.OnUninitialize(world);
        world.Dispatcher.Unlisten<Sid<TId>.SetValue>(OnEntityIdChanged);
    }

    private bool OnEntityIdChanged(in EntityRef entity, in Sid<TId>.SetValue e)
    {
        ref var id = ref entity.Get<Sid<TId>>();
        RemoveMap(entity, id.Previous);
        AddMap(entity, id.Value);
        return false;
    }

    protected override void OnEntityAdded(in EntityRef entity)
    {
        var id = entity.Get<Sid<TId>>().Value;
        AddMap(entity, id);
    }

    protected override void OnEntityRemoved(in EntityRef entity)
    {
        var id = entity.Get<Sid<TId>>().Value;
        RemoveMap(entity, id);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AddMap(in EntityRef entity, in TId id)
    {
        if (id.Equals(default)) {
            return;
        }
        _maps[id] = entity;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void RemoveMap(in EntityRef entity, in TId id)
    {
        if (!_maps.Remove(id, out var removedEntity)) {
            return;
        }
        if (removedEntity != entity) {
            _maps[id] = removedEntity;
        }
    }

    public bool ContainsKey(TId key)
        => _maps.ContainsKey(key);

    public bool TryGetValue(TId key, [MaybeNullWhen(false)] out EntityRef value)
        => _maps.TryGetValue(key, out value);

    public IEnumerator<KeyValuePair<TId, EntityRef>> GetEnumerator()
        => _maps.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator()
        => _maps.GetEnumerator();
}