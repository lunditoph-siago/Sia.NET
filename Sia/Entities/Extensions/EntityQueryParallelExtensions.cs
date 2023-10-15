namespace Sia;

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using CommunityToolkit.HighPerformance.Buffers;

public static class EntityQueryParallelExtensions
{
    private readonly struct ForEachParallelAction
    {
        private readonly EntityRef[] _array;
        private readonly EntityHandler _handler;

        public ForEachParallelAction(EntityRef[] array, EntityHandler handler)
        {
            _array = array;
            _handler = handler;
        }

        public void Invoke(System.Tuple<int, int> range)
        {
            for (int i = range.Item1; i != range.Item2; ++i) {
                _handler(_array[i]);
            }
        }
    }

    private readonly struct ForEachParallelAction<TData>
    {
        private readonly EntityRef[] _array;
        private readonly TData _data;
        private readonly EntityHandler<TData> _handler;

        public ForEachParallelAction(EntityRef[] array, in TData data, EntityHandler<TData> handler)
        {
            _array = array;
            _data = data;
            _handler = handler;
        }

        public void Invoke(System.Tuple<int, int> range)
        {
            for (int i = range.Item1; i != range.Item2; ++i) {
                _handler(_data, _array[i]);
            }
        }
    }

    private unsafe struct ForEachParallelData
    {
        public int* Index;
        public EntityRef[] Array;
        
        public ForEachParallelData(int* index, EntityRef[] array)
        {
            Index = index;
            Array = array;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private unsafe static void RecordEntities(IEntityQuery query, EntityRef[] array)
    {
        int index = 0;
        var indexPtr = (int*)Unsafe.AsPointer(ref index);

        query.ForEach(new(indexPtr, array), static (in ForEachParallelData data, in EntityRef entity) => {
            ref int index = ref *data.Index;
            data.Array[index] = entity;
            ++index;
        });
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void ForEachParallel(this IEntityQuery query, EntityHandler handler)
    {
        var count = query.Count;
        if (count == 0) { return; }

        using var spanOwner = SpanOwner<EntityRef>.Allocate(count);
        var array = spanOwner.DangerousGetArray().Array!;
        RecordEntities(query, array);

        var action = new ForEachParallelAction(array, handler);
        Partitioner.Create(0, count)
            .AsParallel()
            .ForAll(action.Invoke);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void ForEachParallel<TData>(this IEntityQuery query, in TData data, EntityHandler<TData> handler)
    {
        var count = query.Count;
        if (count == 0) { return; }

        using var spanOwner = SpanOwner<EntityRef>.Allocate(count);
        var array = spanOwner.DangerousGetArray().Array!;
        RecordEntities(query, array);

        var action = new ForEachParallelAction<TData>(array, data, handler);
        Partitioner.Create(0, count)
            .AsParallel()
            .ForAll(action.Invoke);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void ForEachParallel(this IEntityQuery query, SimpleEntityHandler handler)
        => ForEachParallel(query, handler,
            static (in SimpleEntityHandler handler, in EntityRef entity) => handler(entity));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void ForEachParallel<TData>(this IEntityQuery query, in TData data, SimpleEntityHandler<TData> handler)
        => ForEachParallel(query, (handler, data),
            static (in (SimpleEntityHandler<TData>, TData) data, in EntityRef entity) => data.Item1(data.Item2, entity));
}