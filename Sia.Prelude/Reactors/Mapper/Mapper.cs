namespace Sia.Reactors;

using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

public class Mapper<TId> : ReactorBase<TypeUnion<Sid<TId>>>, IReadOnlyDictionary<TId, Entity>
    where TId : notnull, IEquatable<TId>
{
    public IEnumerable<TId> Keys => _maps.Keys;
    public IEnumerable<Entity> Values => _maps.Values;

    public int Count => _maps.Count;

    public Entity this[TId key] => _maps[key];

    private readonly Dictionary<TId, Entity> _maps = [];

    public override void OnInitialize(World world)
    {
        base.OnInitialize(world);
        Listen<Sid<TId>.SetValue>(OnEntityIdChanged);
    }

    private bool OnEntityIdChanged(Entity entity, in Sid<TId>.SetValue e)
    {
        ref var id = ref entity.Get<Sid<TId>>();
        RemoveMap(entity, id.Previous);
        AddMap(entity, id.Value);
        return false;
    }

    protected override void OnEntityAdded(Entity entity)
    {
        var id = entity.Get<Sid<TId>>().Value;
        AddMap(entity, id);
    }

    protected override void OnEntityRemoved(Entity entity)
    {
        var id = entity.Get<Sid<TId>>().Value;
        RemoveMap(entity, id);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AddMap(Entity entity, in TId id)
    {
        _maps[id] = entity;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void RemoveMap(Entity entity, in TId id)
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

    public bool TryGetValue(TId key, [MaybeNullWhen(false)] out Entity value)
        => _maps.TryGetValue(key, out value);

    public IEnumerator<KeyValuePair<TId, Entity>> GetEnumerator()
        => _maps.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator()
        => _maps.GetEnumerator();
}