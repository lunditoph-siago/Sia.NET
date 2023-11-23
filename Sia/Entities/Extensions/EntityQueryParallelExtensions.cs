namespace Sia;

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using CommunityToolkit.HighPerformance.Buffers;

public static class EntityQueryParallelExtensions
{
    public delegate void RecordFunc<TResult>(in EntityRef entity, ref TResult result);
    public delegate void RecordFunc<TData, TResult>(in TData data, in EntityRef entity, ref TResult result);

    private readonly struct ForEachParallelAction
    {
        private readonly ArraySegment<EntityRef> _array;
        private readonly EntityHandler _handler;

        public ForEachParallelAction(ArraySegment<EntityRef> array, EntityHandler handler)
        {
            _array = array;
            _handler = handler;
        }

        public void Invoke(System.Tuple<int, int> range)
        {
            var span = _array.AsSpan();
            for (int i = range.Item1; i != range.Item2; ++i) {
                _handler(span[i]);
            }
        }
    }

    private readonly struct ForEachParallelAction<TData>
    {
        private readonly ArraySegment<EntityRef> _array;
        private readonly TData _data;
        private readonly EntityHandler<TData> _handler;

        public ForEachParallelAction(ArraySegment<EntityRef> array, in TData data, EntityHandler<TData> handler)
        {
            _array = array;
            _data = data;
            _handler = handler;
        }

        public void Invoke(System.Tuple<int, int> range)
        {
            var span = _array.AsSpan();
            for (int i = range.Item1; i != range.Item2; ++i) {
                _handler(_data, span[i]);
            }
        }
    }

    private readonly unsafe struct RecordData
    {
        public readonly int* Index;
        public readonly ArraySegment<EntityRef> Array;
        
        public RecordData(int* index, ArraySegment<EntityRef> array)
        {
            Index = index;
            Array = array;
        }
    }

    private readonly unsafe struct RecordData<T>
    {
        public readonly int* Index;
        public readonly ArraySegment<T> Array;
        public readonly RecordFunc<T> RecordFunc;
        
        public RecordData(int* index, ArraySegment<T> array, RecordFunc<T> recordFunc)
        {
            Index = index;
            Array = array;
            RecordFunc = recordFunc;
        }
    }

    private readonly unsafe struct RecordData<TData, TResult>
    {
        public readonly int* Index;
        public readonly TData Data;
        public readonly ArraySegment<TResult> Array;
        public readonly RecordFunc<TData, TResult> RecordFunc;
        
        public RecordData(int* index, in TData data, ArraySegment<TResult> array, RecordFunc<TData, TResult> recordFunc)
        {
            Index = index;
            Data = data;
            Array = array;
            RecordFunc = recordFunc;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void ForEachParallel(this IEntityQuery query, EntityHandler handler)
    {
        var count = query.Count;
        if (count == 0) { return; }

        var spanOwner = SpanOwner<EntityRef>.Allocate(count);
        var array = spanOwner.DangerousGetArray();
        Record(query, array);

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

        var spanOwner = SpanOwner<EntityRef>.Allocate(count);
        var array = spanOwner.DangerousGetArray();
        Record(query, array);

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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe SpanOwner<EntityRef> Record(this IEntityQuery query)
    {
        var count = query.Count;
        if (count == 0) { return default; }

        var spanOwner = SpanOwner<EntityRef>.Allocate(count);
        Record(query, spanOwner.DangerousGetArray());
        return spanOwner;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe SpanOwner<TResult> Record<TResult>(
        this IEntityQuery query, RecordFunc<TResult> recordFunc)
    {
        var count = query.Count;
        if (count == 0) { return default; }

        var spanOwner = SpanOwner<TResult>.Allocate(count);
        Record(query, spanOwner.DangerousGetArray(), recordFunc);
        return spanOwner;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe SpanOwner<TResult> Record<TData, TResult>(
        this IEntityQuery query, in TData data, RecordFunc<TData, TResult> recordFunc)
    {
        var count = query.Count;
        if (count == 0) { return default; }

        var spanOwner = SpanOwner<TResult>.Allocate(count);
        Record(query, data, spanOwner.DangerousGetArray(), recordFunc);
        return spanOwner;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void Record(IEntityQuery query, ArraySegment<EntityRef> array)
    {
        int index = 0;
        var indexPtr = (int*)Unsafe.AsPointer(ref index);

        query.ForEach(new(indexPtr, array), static (in RecordData data, in EntityRef entity) => {
            ref int index = ref *data.Index;
            data.Array[index] = entity;
            ++index;
        });
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void Record<TResult>(
        IEntityQuery query, ArraySegment<TResult> array, RecordFunc<TResult> recordFunc)
    {
        int index = 0;
        var indexPtr = (int*)Unsafe.AsPointer(ref index);

        query.ForEach(new(indexPtr, array, recordFunc), static (in RecordData<TResult> data, in EntityRef entity) => {
            ref int index = ref *data.Index;
            data.RecordFunc(entity, ref data.Array.AsSpan()[index]);
            ++index;
        });
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void Record<TData, TResult>(
        IEntityQuery query, in TData data, ArraySegment<TResult> array, RecordFunc<TData, TResult> recordFunc)
    {
        int index = 0;
        var indexPtr = (int*)Unsafe.AsPointer(ref index);

        query.ForEach(new(indexPtr, data, array, recordFunc), static (in RecordData<TData, TResult> data, in EntityRef entity) => {
            ref int index = ref *data.Index;
            data.RecordFunc(data.Data, entity, ref data.Array.AsSpan()[index]);
            ++index;
        });
    }
}