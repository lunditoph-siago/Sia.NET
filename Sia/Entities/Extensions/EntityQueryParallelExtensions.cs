namespace Sia;

using System.Runtime.CompilerServices;
using CommunityToolkit.HighPerformance.Buffers;

public static class EntityQueryParallelExtensions
{
    public delegate void RecordFunc<TResult>(in EntityRef entity, ref TResult result);
    public delegate void RecordFunc<TData, TResult>(in TData data, in EntityRef entity, ref TResult result);
    public delegate bool CondRecordFunc<TResult>(in EntityRef entity, ref TResult result);
    public delegate bool CondRecordFunc<TData, TResult>(in TData data, in EntityRef entity, ref TResult result);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static (IEntityHost Host, int From, int To) FindHostRange(IEntityQuery query, (int, int) range)
    {
        (int from, int to) = range;

        var hosts = query.Hosts;
        IEntityHost host;

        int hostIndex = 0;
        int counter = 0;
        int prevCounter;

        do {
            host = hosts[hostIndex];
            prevCounter = counter;
            counter += host.Count;
            hostIndex++;
        } while (counter < from);

        return (host, from - prevCounter, to - prevCounter);
    }

    private class ForEachParallelAction(
        IEntityQuery Query, EntityHandler Handler)
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Invoke(in (int, int) range)
        {
            var (host, from, to) = FindHostRange(Query, range);
            var slots = host.AllocatedSlots;

            for (int i = from; i != to; ++i) {
                Handler(new(slots[i], host));
            }
        }
    }

    private class ForEachParallelAction<TData>(
        IEntityQuery Query, TData Data, EntityHandler<TData> Handler)
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Invoke(in (int, int) range)
        {
            var (host, from, to) = FindHostRange(Query, range);
            var slots = host.AllocatedSlots;

            for (int i = from; i != to; ++i) {
                Handler(Data, new(slots[i], host));
            }
        }
    }

    private readonly unsafe struct RecordData(int* index, ArraySegment<EntityRef> array)
    {
        public readonly int* Index = index;
        public readonly ArraySegment<EntityRef> Array = array;
    }

    private readonly unsafe struct RecordData<T>(int* index, ArraySegment<T> array, RecordFunc<T> recordFunc)
    {
        public readonly int* Index = index;
        public readonly ArraySegment<T> Array = array;
        public readonly RecordFunc<T> RecordFunc = recordFunc;
    }

    private readonly unsafe struct RecordData<TData, TResult>(int* index, in TData data, ArraySegment<TResult> array, EntityQueryParallelExtensions.RecordFunc<TData, TResult> recordFunc)
    {
        public readonly int* Index = index;
        public readonly TData Data = data;
        public readonly ArraySegment<TResult> Array = array;
        public readonly RecordFunc<TData, TResult> RecordFunc = recordFunc;
    }

    private readonly unsafe struct CondRecordData<T>(int* index, ArraySegment<T> array, CondRecordFunc<T> recordFunc)
    {
        public readonly int* Index = index;
        public readonly ArraySegment<T> Array = array;
        public readonly CondRecordFunc<T> RecordFunc = recordFunc;
    }

    private readonly unsafe struct CondRecordData<TData, TResult>(int* index, in TData data, ArraySegment<TResult> array, CondRecordFunc<TData, TResult> recordFunc)
    {
        public readonly int* Index = index;
        public readonly TData Data = data;
        public readonly ArraySegment<TResult> Array = array;
        public readonly CondRecordFunc<TData, TResult> RecordFunc = recordFunc;
    }

    private static readonly ThreadLocal<Stack<Task[]>> s_taskArrayPool = new(() => []);

    private static readonly int DegreeOfParallelism = Environment.ProcessorCount;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void ForEachParallel(
        this IEntityQuery query, EntityHandler handler)
    {
        var count = query.Count;
        if (count == 0) { return; }

        var div = count / DegreeOfParallelism;
        var remaining = count % DegreeOfParallelism;
        var acc = 0;

        var pool = s_taskArrayPool.Value!;
        if (!pool.TryPop(out var tasks)) {
            tasks = new Task[DegreeOfParallelism];
        }

        var action = new ForEachParallelAction(query, handler);

        for (int i = 0; i != DegreeOfParallelism; ++i) {
            int start = acc;
            acc += i < remaining ? div + 1 : div;
            var end = acc;
            tasks[i] = Task.Run(() => action.Invoke((start, end)));
        }

        Task.WaitAll(tasks);

        Array.Clear(tasks);
        pool.Push(tasks);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void ForEachParallel<TData>(
        this IEntityQuery query, in TData data, EntityHandler<TData> handler)
    {
        var count = query.Count;
        if (count == 0) { return; }

        var div = count / DegreeOfParallelism;
        var remaining = count % DegreeOfParallelism;
        var acc = 0;

        var pool = s_taskArrayPool.Value!;
        if (!pool.TryPop(out var tasks)) {
            tasks = new Task[DegreeOfParallelism];
        }

        var action = new ForEachParallelAction<TData>(query, data, handler);

        for (int i = 0; i != DegreeOfParallelism; ++i) {
            int start = acc;
            acc += i < remaining ? div + 1 : div;
            var end = acc;
            tasks[i] = Task.Run(() => action.Invoke((start, end)));
        }

        Task.WaitAll(tasks);

        Array.Clear(tasks);
        pool.Push(tasks);
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
        if (count == 0) { return SpanOwner<EntityRef>.Empty; }

        var spanOwner = SpanOwner<EntityRef>.Allocate(count);
        Record(query, spanOwner.DangerousGetArray());
        return spanOwner;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe SpanOwner<TResult> Record<TResult>(
        this IEntityQuery query, RecordFunc<TResult> recordFunc)
    {
        var count = query.Count;
        if (count == 0) { return SpanOwner<TResult>.Empty; }

        var spanOwner = SpanOwner<TResult>.Allocate(count);
        Record(query, spanOwner.DangerousGetArray(), recordFunc);
        return spanOwner;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe SpanOwner<TResult> Record<TData, TResult>(
        this IEntityQuery query, in TData data, RecordFunc<TData, TResult> recordFunc)
    {
        var count = query.Count;
        if (count == 0) { return SpanOwner<TResult>.Empty; }

        var spanOwner = SpanOwner<TResult>.Allocate(count);
        Record(query, data, spanOwner.DangerousGetArray(), recordFunc);
        return spanOwner;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe SpanOwner<TResult> Record<TResult>(
        this IEntityQuery query, CondRecordFunc<TResult> recordFunc, out int count)
    {
        var queryCount = query.Count;
        if (queryCount == 0) {
            count = 0;
            return SpanOwner<TResult>.Empty;
        }

        var spanOwner = SpanOwner<TResult>.Allocate(queryCount);
        count = Record(query, spanOwner.DangerousGetArray(), recordFunc);
        return spanOwner;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe SpanOwner<TResult> Record<TData, TResult>(
        this IEntityQuery query, in TData data, CondRecordFunc<TData, TResult> recordFunc, out int count)
    {
        var queryCount = query.Count;
        if (queryCount == 0) {
            count = 0;
            return SpanOwner<TResult>.Empty;
        }

        var spanOwner = SpanOwner<TResult>.Allocate(queryCount);
        count = Record(query, data, spanOwner.DangerousGetArray(), recordFunc);
        return spanOwner;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void Record(this IEntityQuery query, MemoryOwner<EntityRef> memory)
        => Record(query, memory.DangerousGetArray());

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void Record<TResult>(
        this IEntityQuery query, MemoryOwner<TResult> memory, RecordFunc<TResult> recordFunc)
        => Record(query, memory.DangerousGetArray(), recordFunc);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static int Record<TResult>(
        this IEntityQuery query, MemoryOwner<TResult> memory, CondRecordFunc<TResult> recordFunc)
        => Record(query, memory.DangerousGetArray(), recordFunc);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void Record<TData, TResult>(
        this IEntityQuery query, in TData data, MemoryOwner<TResult> memory, RecordFunc<TData, TResult> recordFunc)
        => Record(query, data, memory.DangerousGetArray(), recordFunc);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void Record(this IEntityQuery query, ArraySegment<EntityRef> array)
    {
        int index = 0;
        foreach (var entity in query) {
            array[index] = entity;
            index++;
        }
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void Record<TResult>(
        this IEntityQuery query, ArraySegment<TResult> array, RecordFunc<TResult> recordFunc)
    {
        int index = 0;
        var span = array.AsSpan();
        foreach (var entity in query) {
            recordFunc(entity, ref span[index]);
            index++;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void Record<TData, TResult>(
        this IEntityQuery query, in TData data, ArraySegment<TResult> array, RecordFunc<TData, TResult> recordFunc)
    {
        int index = 0;
        var span = array.AsSpan();
        foreach (var entity in query) {
            recordFunc(data, entity, ref span[index]);
            index++;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static int Record<TResult>(
        this IEntityQuery query, ArraySegment<TResult> array, CondRecordFunc<TResult> recordFunc)
    {
        int index = 0;
        var span = array.AsSpan();
        foreach (var entity in query) {
            if (recordFunc(entity, ref span[index])) {
                index++;
            }
        }
        return index;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static int Record<TData, TResult>(
        this IEntityQuery query, in TData data, ArraySegment<TResult> array, CondRecordFunc<TData, TResult> recordFunc)
    {
        int index = 0;
        var span = array.AsSpan();
        foreach (var entity in query) {
            if (recordFunc(data, entity, ref span[index])) {
                index++;
            }
        }
        return index;
    }
}