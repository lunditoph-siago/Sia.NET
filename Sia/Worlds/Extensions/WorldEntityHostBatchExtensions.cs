using System.Buffers;
using System.Runtime.CompilerServices;

namespace Sia;

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
                && arraySource.InnerHost.GetType() == typeof(ArrayEntityHost<TEntity>)) {
                if (arraySource.InnerHost.TryBulkAddAll<TComponent>(arraySource, copiedValues.AsSpan(0, count))) return;
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
        => RemoveHeadComponentFromAll<HList<THead, TTail>, THead, TTail, TInnerHost>(source);

    public static void RemoveHeadComponentFromAll<THead, TInnerHost>(
        this WorldEntityHost<HList<THead>, TInnerHost> source)
        where TInnerHost : IEntityHost<HList<THead>>, new()
        => RemoveHeadComponentFromAll<HList<THead>, THead, EmptyHList, TInnerHost>(source);

    private static void RemoveHeadComponentFromAll<TEntity, THead, TTail, TInnerHost>(
        WorldEntityHost<TEntity, TInnerHost> source)
        where TEntity : struct, IHList
        where TTail : struct, IHList
        where TInnerHost : IEntityHost<TEntity>, new()
    {
        ArgumentNullException.ThrowIfNull(source);
        var world = source.World;
        world.EnsureOwnerThread();
        var count = source.Count;
        if (count == 0) return;
        if (source is WorldEntityHost<TEntity, ArrayEntityHost<TEntity>> arraySource
            && arraySource.InnerHost.GetType() == typeof(ArrayEntityHost<TEntity>)
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
