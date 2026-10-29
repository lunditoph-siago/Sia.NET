namespace Sia;

using System.Buffers;
using System.Runtime.CompilerServices;

public static class WorldEntityHostBatchExtensions
{
    /// <summary>
    /// Adds one component value to each entity currently in the host. Values are
    /// snapshotted and correspond to the host's entity order at the start of the call.
    /// Observers receive the same per-entity events as individual additions.
    /// </summary>
    /// <remarks>
    /// Run on the world's owner thread. If an observer throws or changes a later
    /// entity, earlier changes are not rolled back and the operation stops at the
    /// first error. Do not access the world concurrently; reacquire borrowed
    /// component references and entity spans after this structural change.
    /// </remarks>
    /// <exception cref="ArgumentException">The number of values differs from the host's entity count.</exception>
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
                var current = arraySource.InnerHost.UnsafeGetEntitySpan();
                if (current.Length == count && arraySource.InnerHost.TryBulkAddAll(arraySource, target, current, copiedValues.AsSpan(0, count))) return;
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

    /// <summary>
    /// Removes the head component from every entity currently in the host, in
    /// its initial entity order, preserving per-entity events when observed.
    /// </summary>
    /// <remarks>
    /// Run on the world's owner thread. If an observer throws or changes a later
    /// entity, earlier changes are not rolled back and the operation stops at the
    /// first error. Do not access the world concurrently; reacquire borrowed
    /// component references and entity spans after this structural change.
    /// </remarks>
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
            var current = arraySource.InnerHost.UnsafeGetEntitySpan();
            if (current.Length == count && arraySource.InnerHost.TryBulkRemoveHeadAll<THead, TTail>(arraySource, target, current)) return;
        }
        var entities = ArrayPool<Entity>.Shared.Rent(count);
        try {
            source.UnsafeGetEntitySpan()[..count].CopyTo(entities);
            for (var i = 0; i < count; i++) entities[i].Remove<THead>();
        }
        finally { ArrayPool<Entity>.Shared.Return(entities, clearArray: true); }
    }
}
