namespace Sia;

using System.Runtime.CompilerServices;
using CommunityToolkit.HighPerformance.Buffers;
using CommunityToolkit.HighPerformance.Helpers;

public static class EntityQueryParallelExtensions
{
    #pragma warning disable CS8500

    public unsafe readonly struct ForEachParallelAction : IAction
    {
        private readonly EntityRef* _memory;
        private readonly EntityHandler _handler;

        public ForEachParallelAction(EntityRef* memory, EntityHandler handler)
        {
            _memory = memory;
            _handler = handler;
        }

        public void Invoke(int index)
            => _handler(_memory[index]);
    }

    public unsafe readonly struct ForEachParallelAction<TData> : IAction
    {
        private readonly EntityRef* _memory;
        private readonly TData _data;
        private readonly EntityHandler<TData> _handler;

        public ForEachParallelAction(EntityRef* memory, in TData data, EntityHandler<TData> handler)
        {
            _memory = memory;
            _data = data;
            _handler = handler;
        }

        public void Invoke(int index)
            => _handler(_data, _memory[index]);
    }

    public unsafe struct ForEachParallelData
    {
        public int* Index;
        public EntityRef* Memory;
        
        public ForEachParallelData(int* index, EntityRef* memory)
        {
            Index = index;
            Memory = memory;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void ForEachParallel(this IEntityQuery query, EntityHandler handler, int minimumActionsPerThread = 1)
    {
        var count = query.Count;
        if (count == 0) { return; }

        using var spanOwner = SpanOwner<EntityRef>.Allocate(count);
        var memory = (EntityRef*)Unsafe.AsPointer(ref spanOwner.Span[0]);

        int index = 0;
        var indexPtr = (int*)Unsafe.AsPointer(ref index);

        query.ForEach(new(indexPtr, memory), static (in ForEachParallelData span, in EntityRef entity) => {
            ref int index = ref *span.Index;
            span.Memory[index] = entity;
            ++index;
        });

        ParallelHelper.For(0, count, new ForEachParallelAction(memory, handler), minimumActionsPerThread);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void ForEachParallel<TData>(this IEntityQuery query, in TData data, EntityHandler<TData> handler, int minimumActionsPerThread = 1)
    {
        var count = query.Count;
        if (count == 0) { return; }

        using var spanOwner = SpanOwner<EntityRef>.Allocate(count);
        var memory = (EntityRef*)Unsafe.AsPointer(ref spanOwner.Span[0]);

        int index = 0;
        var indexPtr = (int*)Unsafe.AsPointer(ref index);

        query.ForEach(new(indexPtr, memory), static (in ForEachParallelData span, in EntityRef entity) => {
            ref int index = ref *span.Index;
            span.Memory[index] = entity;
            ++index;
        });

        ParallelHelper.For(0, count, new ForEachParallelAction<TData>(memory, data, handler), minimumActionsPerThread);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void ForEachParallel(this IEntityQuery query, SimpleEntityHandler handler, int minimumActionsPerThread = 1)
        => ForEachParallel(query, handler,
            static (in SimpleEntityHandler handler, in EntityRef entity) => handler(entity),
            minimumActionsPerThread);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void ForEachParallel<TData>(this IEntityQuery query, in TData data, SimpleEntityHandler<TData> handler, int minimumActionsPerThread = 1)
        => ForEachParallel(query, (handler, data),
            static (in (SimpleEntityHandler<TData>, TData) data, in EntityRef entity) => data.Item1(data.Item2, entity),
            minimumActionsPerThread);

    #pragma warning restore CS8500
}