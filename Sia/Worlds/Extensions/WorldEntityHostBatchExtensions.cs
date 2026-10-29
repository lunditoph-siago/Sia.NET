namespace Sia;

using System.Buffers;
using System.Runtime.CompilerServices;

public static class WorldEntityHostBatchExtensions
{
    public static void AddComponentToAll<TEntity, TInnerHost, TComponent>(
        this WorldEntityHost<TEntity, TInnerHost> source,
        ReadOnlySpan<TComponent> values)
        where TEntity : struct, IHList
        where TInnerHost : IEntityHost<TEntity>, new()
    {
        ArgumentNullException.ThrowIfNull(source);
        var world = source.World;
        world.EnsureOwnerThread();
        var count = source.Count;
        if (values.Length != count) throw new ArgumentException("A value is required for each entity.", nameof(values));
        if (count == 0) return;

        var copiedValues = ArrayPool<TComponent>.Shared.Rent(count);
        try {
            values.CopyTo(copiedValues);
            if (source is WorldEntityHost<TEntity, ArrayEntityHost<TEntity>> arraySource
                && arraySource.InnerHost.GetType() == typeof(ArrayEntityHost<TEntity>)
                && world.TryGetHost<HList<TComponent, TEntity>, ArrayEntityHost<HList<TComponent, TEntity>>>(out var target)) {
                if (arraySource.InnerHost.TryBulkAddAll(arraySource, target, copiedValues.AsSpan(0, count))) return;
            }
            var entities = ArrayPool<Entity>.Shared.Rent(count);
            try {
                source.UnsafeGetEntitySpan()[..count].CopyTo(entities);
                for (var i = 0; i < count; i++) entities[i].Add(copiedValues[i]);
            }
            finally { ArrayPool<Entity>.Shared.Return(entities, clearArray: true); }
        }
        finally {
            ArrayPool<TComponent>.Shared.Return(copiedValues,
                clearArray: RuntimeHelpers.IsReferenceOrContainsReferences<TComponent>());
        }
    }

    public static void RemoveHeadComponentFromAll<THead, TTail, TInnerHost>(
        this WorldEntityHost<HList<THead, TTail>, TInnerHost> source)
        where TTail : struct, IHList
        where TInnerHost : IEntityHost<HList<THead, TTail>>, new()
    {
        ArgumentNullException.ThrowIfNull(source);
        var world = source.World;
        world.EnsureOwnerThread();
        var count = source.Count;
        if (count == 0) return;
        if (source is WorldEntityHost<HList<THead, TTail>, ArrayEntityHost<HList<THead, TTail>>> arraySource
            && arraySource.InnerHost.GetType() == typeof(ArrayEntityHost<HList<THead, TTail>>)
            && world.TryGetHost<TTail, ArrayEntityHost<TTail>>(out var target)) {
            if (arraySource.InnerHost.TryBulkRemoveHeadAll<THead, TTail>(arraySource, target)) return;
        }
        var entities = ArrayPool<Entity>.Shared.Rent(count);
        try {
            source.UnsafeGetEntitySpan()[..count].CopyTo(entities);
            for (var i = 0; i < count; i++) entities[i].Remove<THead>();
        }
        finally { ArrayPool<Entity>.Shared.Return(entities, clearArray: true); }
    }
}
