namespace Sia;

using System.Runtime.CompilerServices;

public static partial class EntityHostExtensions
{
    #region ForEach

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void ForEach<TRunner>(
        this IEntityHost host, EntityHandler handler, TRunner runner, RunnerBarrier? barrier)
        where TRunner : IRunner
        => host.Handle(handler,
            static (IEntityHost host, in EntityHandler handler, int from, int to) => {
                for (int i = from; i != to; ++i) {
                    handler(host.GetEntity(i));
                }
            }, runner, barrier);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void ForEach<TRunner, TData>(
        this IEntityHost host, in TData userData, EntityHandler<TData> handler, TRunner runner, RunnerBarrier? barrier)
        where TRunner : IRunner
        => host.Handle((userData, handler),
            static (IEntityHost host, in (TData, EntityHandler<TData>) entry, int from, int to) => {
                ref readonly var data = ref entry.Item1;
                var handler = entry.Item2;
                for (int i = from; i != to; ++i) {
                    handler(data, host.GetEntity(i));
                }
            }, runner, barrier);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void ForEach<TRunner>(
        this IEntityHost host, SimpleEntityHandler handler, TRunner runner, RunnerBarrier? barrier)
        where TRunner : IRunner
        => host.ForEach(handler,
            static (in SimpleEntityHandler handler, Entity entity)
                => handler(entity), runner, barrier);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void ForEach<TRunner, TData>(
        this IEntityHost host, in TData userData, SimpleEntityHandler<TData> handler, TRunner runner, RunnerBarrier? barrier)
        where TRunner : IRunner
        => host.ForEach((handler, userData),
            static (in (SimpleEntityHandler<TData>, TData) data, Entity entity)
                => data.Item1(data.Item2, entity), runner, barrier);

    #region CurrentThreadRunner

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void ForEach(this IEntityHost host, EntityHandler handler)
        => host.ForEach(handler, CurrentThreadRunner.Instance, barrier: null);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void ForEach<TData>(
        this IEntityHost host, in TData data, EntityHandler<TData> handler)
        => host.ForEach(data, handler, CurrentThreadRunner.Instance, barrier: null);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void ForEach(this IEntityHost host, SimpleEntityHandler handler)
        => host.ForEach(handler, CurrentThreadRunner.Instance, barrier: null);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void ForEach<TData>(
        this IEntityHost host, in TData data, SimpleEntityHandler<TData> handler)
        => host.ForEach(data, handler, CurrentThreadRunner.Instance, barrier: null);
    
    #endregion // CurrentThreadRunner

   #region ParallelRunner

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void ForEachOnParallel(this IEntityHost host, EntityHandler handler)
    {
        var barrier = RunnerBarrier.Get();
        host.ForEach(handler, ParallelRunner.Default, barrier);
        barrier.WaitAndReturn();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void ForEachOnParallel<TData>(
        this IEntityHost host, in TData data, EntityHandler<TData> handler)
    {
        var barrier = RunnerBarrier.Get();
        host.ForEach(data, handler, ParallelRunner.Default, barrier);
        barrier.WaitAndReturn();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void ForEachOnParallel(this IEntityHost host, SimpleEntityHandler handler)
    {
        var barrier = RunnerBarrier.Get();
        host.ForEach(handler, ParallelRunner.Default, barrier);
        barrier.WaitAndReturn();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void ForEachOnParallel<TData>(
        this IEntityHost host, in TData data, SimpleEntityHandler<TData> handler)
    {
        var barrier = RunnerBarrier.Get();
        host.ForEach(data, handler, ParallelRunner.Default, barrier);
        barrier.WaitAndReturn();
    }
    
    #endregion // ParallelRunner

    #endregion // ForEach

    #region ForSlice

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSlice<TRunner, C1>(
        this IEntityHost host, ComponentHandler<C1> handler, TRunner runner, RunnerBarrier? barrier)
        where TRunner : IRunner
        => host.Handle(handler,
            static (IEntityHost host, in ComponentHandler<C1> handler, int from, int to) => {
                var desc = host.Descriptor;
                var c1Offset = desc.GetOffset<C1>();

                if (host is ISequentialEntityHost seqHost) {
                    ref byte memRef = ref seqHost.Bytes[0];
                    var size = seqHost.Descriptor.MemorySize;
                    for (int i = from; i != to; ++i) {
                        nint offset = i * size;
                        handler(ref c1Offset.Get(ref memRef, offset));
                    }
                }
                else {
                    for (int i = from; i != to; ++i) {
                        ref var byteRef = ref host.GetByteRef(i);
                        handler(ref c1Offset.Get(ref byteRef));
                    }
                }
            }, runner, barrier);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSlice<TRunner, C1, C2>(
        this IEntityHost host, ComponentHandler<C1, C2> handler, TRunner runner, RunnerBarrier? barrier)
        where TRunner : IRunner
        => host.Handle(handler,
            static (IEntityHost host, in ComponentHandler<C1, C2> handler, int from, int to) => {
                var desc = host.Descriptor;
                var c1Offset = desc.GetOffset<C1>();
                var c2Offset = desc.GetOffset<C2>();

                if (host is ISequentialEntityHost seqHost) {
                    ref byte memRef = ref seqHost.Bytes[0];
                    var size = seqHost.Descriptor.MemorySize;
                    for (int i = from; i != to; ++i) {
                        nint offset = i * size;
                        handler(
                            ref c1Offset.Get(ref memRef, offset),
                            ref c2Offset.Get(ref memRef, offset));
                    }
                }
                else {
                    for (int i = from; i != to; ++i) {
                        ref var byteRef = ref host.GetByteRef(i);
                        handler(
                            ref c1Offset.Get(ref byteRef),
                            ref c2Offset.Get(ref byteRef));
                    }
                }
            }, runner, barrier);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSlice<TRunner, C1, C2, C3>(
        this IEntityHost host, ComponentHandler<C1, C2, C3> handler, TRunner runner, RunnerBarrier? barrier)
        where TRunner : IRunner
        => host.Handle(handler,
            static (IEntityHost host, in ComponentHandler<C1, C2, C3> handler, int from, int to) => {
                var desc = host.Descriptor;
                var c1Offset = desc.GetOffset<C1>();
                var c2Offset = desc.GetOffset<C2>();
                var c3Offset = desc.GetOffset<C3>();

                if (host is ISequentialEntityHost seqHost) {
                    ref byte memRef = ref seqHost.Bytes[0];
                    var size = seqHost.Descriptor.MemorySize;
                    for (int i = from; i != to; ++i) {
                        nint offset = i * size;
                        handler(
                            ref c1Offset.Get(ref memRef, offset),
                            ref c2Offset.Get(ref memRef, offset),
                            ref c3Offset.Get(ref memRef, offset));
                    }
                }
                else {
                    for (int i = from; i != to; ++i) {
                        ref var byteRef = ref host.GetByteRef(i);
                        handler(
                            ref c1Offset.Get(ref byteRef),
                            ref c2Offset.Get(ref byteRef),
                            ref c3Offset.Get(ref byteRef));
                    }
                }
            }, runner, barrier);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSlice<TRunner, C1, C2, C3, C4>(
        this IEntityHost host, ComponentHandler<C1, C2, C3, C4> handler, TRunner runner, RunnerBarrier? barrier)
        where TRunner : IRunner
        => host.Handle(handler,
            static (IEntityHost host, in ComponentHandler<C1, C2, C3, C4> handler, int from, int to) => {
                var desc = host.Descriptor;
                var c1Offset = desc.GetOffset<C1>();
                var c2Offset = desc.GetOffset<C2>();
                var c3Offset = desc.GetOffset<C3>();
                var c4Offset = desc.GetOffset<C4>();

                if (host is ISequentialEntityHost seqHost) {
                    ref byte memRef = ref seqHost.Bytes[0];
                    var size = seqHost.Descriptor.MemorySize;
                    for (int i = from; i != to; ++i) {
                        nint offset = i * size;
                        handler(
                            ref c1Offset.Get(ref memRef, offset),
                            ref c2Offset.Get(ref memRef, offset),
                            ref c3Offset.Get(ref memRef, offset),
                            ref c4Offset.Get(ref memRef, offset));
                    }
                }
                else {
                    for (int i = from; i != to; ++i) {
                        ref var byteRef = ref host.GetByteRef(i);
                        handler(
                            ref c1Offset.Get(ref byteRef),
                            ref c2Offset.Get(ref byteRef),
                            ref c3Offset.Get(ref byteRef),
                            ref c4Offset.Get(ref byteRef));
                    }
                }
            }, runner, barrier);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSlice<TRunner, C1, C2, C3, C4, C5>(
        this IEntityHost host, ComponentHandler<C1, C2, C3, C4, C5> handler, TRunner runner, RunnerBarrier? barrier)
        where TRunner : IRunner
        => host.Handle(handler,
            static (IEntityHost host, in ComponentHandler<C1, C2, C3, C4, C5> handler, int from, int to) => {
                var desc = host.Descriptor;
                var c1Offset = desc.GetOffset<C1>();
                var c2Offset = desc.GetOffset<C2>();
                var c3Offset = desc.GetOffset<C3>();
                var c4Offset = desc.GetOffset<C4>();
                var c5Offset = desc.GetOffset<C5>();

                if (host is ISequentialEntityHost seqHost) {
                    ref byte memRef = ref seqHost.Bytes[0];
                    var size = seqHost.Descriptor.MemorySize;
                    for (int i = from; i != to; ++i) {
                        nint offset = i * size;
                        handler(
                            ref c1Offset.Get(ref memRef, offset),
                            ref c2Offset.Get(ref memRef, offset),
                            ref c3Offset.Get(ref memRef, offset),
                            ref c4Offset.Get(ref memRef, offset),
                            ref c5Offset.Get(ref memRef, offset));
                    }
                }
                else {
                    for (int i = from; i != to; ++i) {
                        ref var byteRef = ref host.GetByteRef(i);
                        handler(
                            ref c1Offset.Get(ref byteRef),
                            ref c2Offset.Get(ref byteRef),
                            ref c3Offset.Get(ref byteRef),
                            ref c4Offset.Get(ref byteRef),
                            ref c5Offset.Get(ref byteRef));
                    }
                }
            }, runner, barrier);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSlice<TRunner, C1, C2, C3, C4, C5, C6>(
        this IEntityHost host, ComponentHandler<C1, C2, C3, C4, C5, C6> handler, TRunner runner, RunnerBarrier? barrier)
        where TRunner : IRunner
        => host.Handle(handler,
            static (IEntityHost host, in ComponentHandler<C1, C2, C3, C4, C5, C6> handler, int from, int to) => {
                var desc = host.Descriptor;
                var c1Offset = desc.GetOffset<C1>();
                var c2Offset = desc.GetOffset<C2>();
                var c3Offset = desc.GetOffset<C3>();
                var c4Offset = desc.GetOffset<C4>();
                var c5Offset = desc.GetOffset<C5>();
                var c6Offset = desc.GetOffset<C6>();

                if (host is ISequentialEntityHost seqHost) {
                    ref byte memRef = ref seqHost.Bytes[0];
                    var size = seqHost.Descriptor.MemorySize;
                    for (int i = from; i != to; ++i) {
                        nint offset = i * size;
                        handler(
                            ref c1Offset.Get(ref memRef, offset),
                            ref c2Offset.Get(ref memRef, offset),
                            ref c3Offset.Get(ref memRef, offset),
                            ref c4Offset.Get(ref memRef, offset),
                            ref c5Offset.Get(ref memRef, offset),
                            ref c6Offset.Get(ref memRef, offset));
                    }
                }
                else {
                    for (int i = from; i != to; ++i) {
                        ref var byteRef = ref host.GetByteRef(i);
                        handler(
                            ref c1Offset.Get(ref byteRef),
                            ref c2Offset.Get(ref byteRef),
                            ref c3Offset.Get(ref byteRef),
                            ref c4Offset.Get(ref byteRef),
                            ref c5Offset.Get(ref byteRef),
                            ref c6Offset.Get(ref byteRef));
                    }
                }
            }, runner, barrier);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSlice<TRunner, TData, C1>(
        this IEntityHost host, in TData userData, DataComponentHandler<TData, C1> handler, TRunner runner, RunnerBarrier? barrier)
        where TRunner : IRunner
        => host.Handle((handler, userData),
            static (IEntityHost host, in (DataComponentHandler<TData, C1>, TData) data, int from, int to) => {
                var desc = host.Descriptor;
                var c1Offset = desc.GetOffset<C1>();

                var handler = data.Item1;
                ref readonly var userData = ref data.Item2;

                if (host is ISequentialEntityHost seqHost) {
                    ref byte memRef = ref seqHost.Bytes[0];
                    var size = seqHost.Descriptor.MemorySize;
                    for (int i = from; i != to; ++i) {
                        nint offset = i * size;
                        handler(userData,
                            ref c1Offset.Get(ref memRef, offset));
                    }
                }
                else {
                    for (int i = from; i != to; ++i) {
                        ref var byteRef = ref host.GetByteRef(i);
                        handler(userData, ref c1Offset.Get(ref byteRef));
                    }
                }
            }, runner, barrier);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSlice<TRunner, TData, C1, C2>(
        this IEntityHost host, in TData userData, DataComponentHandler<TData, C1, C2> handler, TRunner runner, RunnerBarrier? barrier)
        where TRunner : IRunner
        => host.Handle((handler, userData),
            static (IEntityHost host, in (DataComponentHandler<TData, C1, C2>, TData) data, int from, int to) => {
                var desc = host.Descriptor;
                var c1Offset = desc.GetOffset<C1>();
                var c2Offset = desc.GetOffset<C2>();

                var handler = data.Item1;
                ref readonly var userData = ref data.Item2;

                if (host is ISequentialEntityHost seqHost) {
                    ref byte memRef = ref seqHost.Bytes[0];
                    var size = seqHost.Descriptor.MemorySize;
                    for (int i = from; i != to; ++i) {
                        nint offset = i * size;
                        handler(userData,
                            ref c1Offset.Get(ref memRef, offset),
                            ref c2Offset.Get(ref memRef, offset));
                    }
                }
                else {
                    for (int i = from; i != to; ++i) {
                        ref var byteRef = ref host.GetByteRef(i);
                        handler(userData,
                            ref c1Offset.Get(ref byteRef),
                            ref c2Offset.Get(ref byteRef));
                    }
                }
            }, runner, barrier);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSlice<TRunner, TData, C1, C2, C3>(
        this IEntityHost host, in TData userData, DataComponentHandler<TData, C1, C2, C3> handler, TRunner runner, RunnerBarrier? barrier)
        where TRunner : IRunner
        => host.Handle((handler, userData),
            static (IEntityHost host, in (DataComponentHandler<TData, C1, C2, C3>, TData) data, int from, int to) => {
                var desc = host.Descriptor;
                var c1Offset = desc.GetOffset<C1>();
                var c2Offset = desc.GetOffset<C2>();
                var c3Offset = desc.GetOffset<C3>();

                var handler = data.Item1;
                ref readonly var userData = ref data.Item2;

                if (host is ISequentialEntityHost seqHost) {
                    ref byte memRef = ref seqHost.Bytes[0];
                    var size = seqHost.Descriptor.MemorySize;
                    for (int i = from; i != to; ++i) {
                        nint offset = i * size;
                        handler(userData,
                            ref c1Offset.Get(ref memRef, offset),
                            ref c2Offset.Get(ref memRef, offset),
                            ref c3Offset.Get(ref memRef, offset));
                    }
                }
                else {
                    for (int i = from; i != to; ++i) {
                        ref var byteRef = ref host.GetByteRef(i);
                        handler(userData,
                            ref c1Offset.Get(ref byteRef),
                            ref c2Offset.Get(ref byteRef),
                            ref c3Offset.Get(ref byteRef));
                    }
                }
            }, runner, barrier);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSlice<TRunner, TData, C1, C2, C3, C4>(
        this IEntityHost host, in TData userData, DataComponentHandler<TData, C1, C2, C3, C4> handler, TRunner runner, RunnerBarrier? barrier)
        where TRunner : IRunner
        => host.Handle((handler, userData),
            static (IEntityHost host, in (DataComponentHandler<TData, C1, C2, C3, C4>, TData) data, int from, int to) => {
                var desc = host.Descriptor;
                var c1Offset = desc.GetOffset<C1>();
                var c2Offset = desc.GetOffset<C2>();
                var c3Offset = desc.GetOffset<C3>();
                var c4Offset = desc.GetOffset<C4>();

                var handler = data.Item1;
                ref readonly var userData = ref data.Item2;

                if (host is ISequentialEntityHost seqHost) {
                    ref byte memRef = ref seqHost.Bytes[0];
                    var size = seqHost.Descriptor.MemorySize;
                    for (int i = from; i != to; ++i) {
                        nint offset = i * size;
                        handler(userData,
                            ref c1Offset.Get(ref memRef, offset),
                            ref c2Offset.Get(ref memRef, offset),
                            ref c3Offset.Get(ref memRef, offset),
                            ref c4Offset.Get(ref memRef, offset));
                    }
                }
                else {
                    for (int i = from; i != to; ++i) {
                        ref var byteRef = ref host.GetByteRef(i);
                        handler(userData,
                            ref c1Offset.Get(ref byteRef),
                            ref c2Offset.Get(ref byteRef),
                            ref c3Offset.Get(ref byteRef),
                            ref c4Offset.Get(ref byteRef));
                    }
                }
            }, runner, barrier);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSlice<TRunner, TData, C1, C2, C3, C4, C5>(
        this IEntityHost host, in TData userData, DataComponentHandler<TData, C1, C2, C3, C4, C5> handler, TRunner runner, RunnerBarrier? barrier)
        where TRunner : IRunner
        => host.Handle((handler, userData),
            static (IEntityHost host, in (DataComponentHandler<TData, C1, C2, C3, C4, C5>, TData) data, int from, int to) => {
                var desc = host.Descriptor;
                var c1Offset = desc.GetOffset<C1>();
                var c2Offset = desc.GetOffset<C2>();
                var c3Offset = desc.GetOffset<C3>();
                var c4Offset = desc.GetOffset<C4>();
                var c5Offset = desc.GetOffset<C5>();

                var handler = data.Item1;
                ref readonly var userData = ref data.Item2;

                if (host is ISequentialEntityHost seqHost) {
                    ref byte memRef = ref seqHost.Bytes[0];
                    var size = seqHost.Descriptor.MemorySize;
                    for (int i = from; i != to; ++i) {
                        nint offset = i * size;
                        handler(userData,
                            ref c1Offset.Get(ref memRef, offset),
                            ref c2Offset.Get(ref memRef, offset),
                            ref c3Offset.Get(ref memRef, offset),
                            ref c4Offset.Get(ref memRef, offset),
                            ref c5Offset.Get(ref memRef, offset));
                    }
                }
                else {
                    for (int i = from; i != to; ++i) {
                        ref var byteRef = ref host.GetByteRef(i);
                        handler(userData,
                            ref c1Offset.Get(ref byteRef),
                            ref c2Offset.Get(ref byteRef),
                            ref c3Offset.Get(ref byteRef),
                            ref c4Offset.Get(ref byteRef),
                            ref c5Offset.Get(ref byteRef));
                    }
                }
            }, runner, barrier);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSlice<TRunner, TData, C1, C2, C3, C4, C5, C6>(
        this IEntityHost host, in TData userData, DataComponentHandler<TData, C1, C2, C3, C4, C5, C6> handler, TRunner runner, RunnerBarrier? barrier)
        where TRunner : IRunner
        => host.Handle((handler, userData),
            static (IEntityHost host, in (DataComponentHandler<TData, C1, C2, C3, C4, C5, C6>, TData) data, int from, int to) => {
                var desc = host.Descriptor;
                var c1Offset = desc.GetOffset<C1>();
                var c2Offset = desc.GetOffset<C2>();
                var c3Offset = desc.GetOffset<C3>();
                var c4Offset = desc.GetOffset<C4>();
                var c5Offset = desc.GetOffset<C5>();
                var c6Offset = desc.GetOffset<C6>();

                var handler = data.Item1;
                ref readonly var userData = ref data.Item2;

                if (host is ISequentialEntityHost seqHost) {
                    ref byte memRef = ref seqHost.Bytes[0];
                    var size = seqHost.Descriptor.MemorySize;
                    for (int i = from; i != to; ++i) {
                        nint offset = i * size;
                        handler(userData,
                            ref c1Offset.Get(ref memRef, offset),
                            ref c2Offset.Get(ref memRef, offset),
                            ref c3Offset.Get(ref memRef, offset),
                            ref c4Offset.Get(ref memRef, offset),
                            ref c5Offset.Get(ref memRef, offset),
                            ref c6Offset.Get(ref memRef, offset));
                    }
                }
                else {
                    for (int i = from; i != to; ++i) {
                        ref var byteRef = ref host.GetByteRef(i);
                        handler(userData,
                            ref c1Offset.Get(ref byteRef),
                            ref c2Offset.Get(ref byteRef),
                            ref c3Offset.Get(ref byteRef),
                            ref c4Offset.Get(ref byteRef),
                            ref c5Offset.Get(ref byteRef),
                            ref c6Offset.Get(ref byteRef));
                    }
                }
            }, runner, barrier);

    #region CurrentThreadRunner

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSlice<C1>(
        this IEntityHost host, ComponentHandler<C1> handler)
        => host.ForSlice(handler, CurrentThreadRunner.Instance, barrier: null);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSlice<C1, C2>(
        this IEntityHost host, ComponentHandler<C1, C2> handler)
        => host.ForSlice(handler, CurrentThreadRunner.Instance, barrier: null);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSlice<C1, C2, C3>(
        this IEntityHost host, ComponentHandler<C1, C2, C3> handler)
        => host.ForSlice(handler, CurrentThreadRunner.Instance, barrier: null);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSlice<C1, C2, C3, C4>(
        this IEntityHost host, ComponentHandler<C1, C2, C3, C4> handler)
        => host.ForSlice(handler, CurrentThreadRunner.Instance, barrier: null);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSlice<C1, C2, C3, C4, C5>(
        this IEntityHost host, ComponentHandler<C1, C2, C3, C4, C5> handler)
        => host.ForSlice(handler, CurrentThreadRunner.Instance, barrier: null);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSlice<C1, C2, C3, C4, C5, C6>(
        this IEntityHost host, ComponentHandler<C1, C2, C3, C4, C5, C6> handler)
        => host.ForSlice(handler, CurrentThreadRunner.Instance, barrier: null);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSlice<TData, C1>(
        this IEntityHost host, in TData userData, DataComponentHandler<TData, C1> handler)
        => host.ForSlice(userData, handler, CurrentThreadRunner.Instance, barrier: null);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSlice<TData, C1, C2>(
        this IEntityHost host, in TData userData, DataComponentHandler<TData, C1, C2> handler)
        => host.ForSlice(userData, handler, CurrentThreadRunner.Instance, barrier: null);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSlice<TData, C1, C2, C3>(
        this IEntityHost host, in TData userData, DataComponentHandler<TData, C1, C2, C3> handler)
        => host.ForSlice(userData, handler, CurrentThreadRunner.Instance, barrier: null);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSlice<TData, C1, C2, C3, C4>(
        this IEntityHost host, in TData userData, DataComponentHandler<TData, C1, C2, C3, C4> handler)
        => host.ForSlice(userData, handler, CurrentThreadRunner.Instance, barrier: null);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSlice<TData, C1, C2, C3, C4, C5>(
        this IEntityHost host, in TData userData, DataComponentHandler<TData, C1, C2, C3, C4, C5> handler)
        => host.ForSlice(userData, handler, CurrentThreadRunner.Instance, barrier: null);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSlice<TData, C1, C2, C3, C4, C5, C6>(
        this IEntityHost host, in TData userData, DataComponentHandler<TData, C1, C2, C3, C4, C5, C6> handler)
        => host.ForSlice(userData, handler, CurrentThreadRunner.Instance, barrier: null);
    
    #endregion // CurrentThreadRunner

    #region ParallelRunner

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSliceOnParallel<C1>(
        this IEntityHost host, ComponentHandler<C1> handler)
    {
        var barrier = RunnerBarrier.Get();
        host.ForSlice(handler, ParallelRunner.Default, barrier);
        barrier.WaitAndReturn();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSliceOnParallel<C1, C2>(
        this IEntityHost host, ComponentHandler<C1, C2> handler)
    {
        var barrier = RunnerBarrier.Get();
        host.ForSlice(handler, ParallelRunner.Default, barrier);
        barrier.WaitAndReturn();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSliceOnParallel<C1, C2, C3>(
        this IEntityHost host, ComponentHandler<C1, C2, C3> handler)
    {
        var barrier = RunnerBarrier.Get();
        host.ForSlice(handler, ParallelRunner.Default, barrier);
        barrier.WaitAndReturn();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSliceOnParallel<C1, C2, C3, C4>(
        this IEntityHost host, ComponentHandler<C1, C2, C3, C4> handler)
    {
        var barrier = RunnerBarrier.Get();
        host.ForSlice(handler, ParallelRunner.Default, barrier);
        barrier.WaitAndReturn();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSliceOnParallel<C1, C2, C3, C4, C5>(
        this IEntityHost host, ComponentHandler<C1, C2, C3, C4, C5> handler)
    {
        var barrier = RunnerBarrier.Get();
        host.ForSlice(handler, ParallelRunner.Default, barrier);
        barrier.WaitAndReturn();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSliceOnParallel<C1, C2, C3, C4, C5, C6>(
        this IEntityHost host, ComponentHandler<C1, C2, C3, C4, C5, C6> handler)
    {
        var barrier = RunnerBarrier.Get();
        host.ForSlice(handler, ParallelRunner.Default, barrier);
        barrier.WaitAndReturn();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSliceOnParallel<TData, C1>(
        this IEntityHost host, in TData userData, DataComponentHandler<TData, C1> handler)
    {
        var barrier = RunnerBarrier.Get();
        host.ForSlice(userData, handler, ParallelRunner.Default, barrier);
        barrier.WaitAndReturn();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSliceOnParallel<TData, C1, C2>(
        this IEntityHost host, in TData userData, DataComponentHandler<TData, C1, C2> handler)
    {
        var barrier = RunnerBarrier.Get();
        host.ForSlice(userData, handler, ParallelRunner.Default, barrier);
        barrier.WaitAndReturn();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSliceOnParallel<TData, C1, C2, C3>(
        this IEntityHost host, in TData userData, DataComponentHandler<TData, C1, C2, C3> handler)
    {
        var barrier = RunnerBarrier.Get();
        host.ForSlice(userData, handler, ParallelRunner.Default, barrier);
        barrier.WaitAndReturn();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSliceOnParallel<TData, C1, C2, C3, C4>(
        this IEntityHost host, in TData userData, DataComponentHandler<TData, C1, C2, C3, C4> handler)
    {
        var barrier = RunnerBarrier.Get();
        host.ForSlice(userData, handler, ParallelRunner.Default, barrier);
        barrier.WaitAndReturn();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSliceOnParallel<TData, C1, C2, C3, C4, C5>(
        this IEntityHost host, in TData userData, DataComponentHandler<TData, C1, C2, C3, C4, C5> handler)
    {
        var barrier = RunnerBarrier.Get();
        host.ForSlice(userData, handler, ParallelRunner.Default, barrier);
        barrier.WaitAndReturn();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSliceOnParallel<TData, C1, C2, C3, C4, C5, C6>(
        this IEntityHost host, in TData userData, DataComponentHandler<TData, C1, C2, C3, C4, C5, C6> handler)
    {
        var barrier = RunnerBarrier.Get();
        host.ForSlice(userData, handler, ParallelRunner.Default, barrier);
        barrier.WaitAndReturn();
    }
    
    #endregion // ParallelRunner

    #endregion // ForSlice

    #region ForSlice_WithEntity

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSlice<TRunner, C1>(
        this IEntityHost host, ComponentHandlerWithEntity<C1> handler, TRunner runner, RunnerBarrier? barrier)
        where TRunner : IRunner
        => host.Handle(handler,
            static (IEntityHost host, in ComponentHandlerWithEntity<C1> handler, int from, int to) => {
                var entities = host.UnsafeGetEntitySpan();
                var desc = host.Descriptor;
                var c1Offset = desc.GetOffset<C1>();

                if (host is ISequentialEntityHost seqHost) {
                    ref byte memRef = ref seqHost.Bytes[0];
                    var size = seqHost.Descriptor.MemorySize;
                    for (int i = from; i != to; ++i) {
                        nint offset = i * size;
                        handler(entities[i], ref c1Offset.Get(ref memRef, offset));
                    }
                }
                else {
                    for (int i = from; i != to; ++i) {
                        ref var byteRef = ref host.GetByteRef(i);
                        handler(entities[i], ref c1Offset.Get(ref byteRef));
                    }
                }
            }, runner, barrier);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSlice<TRunner, C1, C2>(
        this IEntityHost host, ComponentHandlerWithEntity<C1, C2> handler, TRunner runner, RunnerBarrier? barrier)
        where TRunner : IRunner
        => host.Handle(handler,
            static (IEntityHost host, in ComponentHandlerWithEntity<C1, C2> handler, int from, int to) => {
                var entities = host.UnsafeGetEntitySpan();
                var desc = host.Descriptor;
                var c1Offset = desc.GetOffset<C1>();
                var c2Offset = desc.GetOffset<C2>();

                if (host is ISequentialEntityHost seqHost) {
                    ref byte memRef = ref seqHost.Bytes[0];
                    var size = seqHost.Descriptor.MemorySize;
                    for (int i = from; i != to; ++i) {
                        nint offset = i * size;
                        handler(entities[i],
                            ref c1Offset.Get(ref memRef, offset),
                            ref c2Offset.Get(ref memRef, offset));
                    }
                }
                else {
                    for (int i = from; i != to; ++i) {
                        ref var byteRef = ref host.GetByteRef(i);
                        handler(entities[i],
                            ref c1Offset.Get(ref byteRef),
                            ref c2Offset.Get(ref byteRef));
                    }
                }
            }, runner, barrier);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSlice<TRunner, C1, C2, C3>(
        this IEntityHost host, ComponentHandlerWithEntity<C1, C2, C3> handler, TRunner runner, RunnerBarrier? barrier)
        where TRunner : IRunner
        => host.Handle(handler,
            static (IEntityHost host, in ComponentHandlerWithEntity<C1, C2, C3> handler, int from, int to) => {
                var entities = host.UnsafeGetEntitySpan();
                var desc = host.Descriptor;
                var c1Offset = desc.GetOffset<C1>();
                var c2Offset = desc.GetOffset<C2>();
                var c3Offset = desc.GetOffset<C3>();

                if (host is ISequentialEntityHost seqHost) {
                    ref byte memRef = ref seqHost.Bytes[0];
                    var size = seqHost.Descriptor.MemorySize;
                    for (int i = from; i != to; ++i) {
                        nint offset = i * size;
                        handler(entities[i],
                            ref c1Offset.Get(ref memRef, offset),
                            ref c2Offset.Get(ref memRef, offset),
                            ref c3Offset.Get(ref memRef, offset));
                    }
                }
                else {
                    for (int i = from; i != to; ++i) {
                        ref var byteRef = ref host.GetByteRef(i);
                        handler(entities[i],
                            ref c1Offset.Get(ref byteRef),
                            ref c2Offset.Get(ref byteRef),
                            ref c3Offset.Get(ref byteRef));
                    }
                }
            }, runner, barrier);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSlice<TRunner, C1, C2, C3, C4>(
        this IEntityHost host, ComponentHandlerWithEntity<C1, C2, C3, C4> handler, TRunner runner, RunnerBarrier? barrier)
        where TRunner : IRunner
        => host.Handle(handler,
            static (IEntityHost host, in ComponentHandlerWithEntity<C1, C2, C3, C4> handler, int from, int to) => {
                var entities = host.UnsafeGetEntitySpan();
                var desc = host.Descriptor;
                var c1Offset = desc.GetOffset<C1>();
                var c2Offset = desc.GetOffset<C2>();
                var c3Offset = desc.GetOffset<C3>();
                var c4Offset = desc.GetOffset<C4>();

                if (host is ISequentialEntityHost seqHost) {
                    ref byte memRef = ref seqHost.Bytes[0];
                    var size = seqHost.Descriptor.MemorySize;
                    for (int i = from; i != to; ++i) {
                        nint offset = i * size;
                        handler(entities[i],
                            ref c1Offset.Get(ref memRef, offset),
                            ref c2Offset.Get(ref memRef, offset),
                            ref c3Offset.Get(ref memRef, offset),
                            ref c4Offset.Get(ref memRef, offset));
                    }
                }
                else {
                    for (int i = from; i != to; ++i) {
                        ref var byteRef = ref host.GetByteRef(i);
                        handler(entities[i],
                            ref c1Offset.Get(ref byteRef),
                            ref c2Offset.Get(ref byteRef),
                            ref c3Offset.Get(ref byteRef),
                            ref c4Offset.Get(ref byteRef));
                    }
                }
            }, runner, barrier);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSlice<TRunner, C1, C2, C3, C4, C5>(
        this IEntityHost host, ComponentHandlerWithEntity<C1, C2, C3, C4, C5> handler, TRunner runner, RunnerBarrier? barrier)
        where TRunner : IRunner
        => host.Handle(handler,
            static (IEntityHost host, in ComponentHandlerWithEntity<C1, C2, C3, C4, C5> handler, int from, int to) => {
                var entities = host.UnsafeGetEntitySpan();
                var desc = host.Descriptor;
                var c1Offset = desc.GetOffset<C1>();
                var c2Offset = desc.GetOffset<C2>();
                var c3Offset = desc.GetOffset<C3>();
                var c4Offset = desc.GetOffset<C4>();
                var c5Offset = desc.GetOffset<C5>();

                if (host is ISequentialEntityHost seqHost) {
                    ref byte memRef = ref seqHost.Bytes[0];
                    var size = seqHost.Descriptor.MemorySize;
                    for (int i = from; i != to; ++i) {
                        nint offset = i * size;
                        handler(entities[i],
                            ref c1Offset.Get(ref memRef, offset),
                            ref c2Offset.Get(ref memRef, offset),
                            ref c3Offset.Get(ref memRef, offset),
                            ref c4Offset.Get(ref memRef, offset),
                            ref c5Offset.Get(ref memRef, offset));
                    }
                }
                else {
                    for (int i = from; i != to; ++i) {
                        ref var byteRef = ref host.GetByteRef(i);
                        handler(entities[i],
                            ref c1Offset.Get(ref byteRef),
                            ref c2Offset.Get(ref byteRef),
                            ref c3Offset.Get(ref byteRef),
                            ref c4Offset.Get(ref byteRef),
                            ref c5Offset.Get(ref byteRef));
                    }
                }
            }, runner, barrier);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSlice<TRunner, C1, C2, C3, C4, C5, C6>(
        this IEntityHost host, ComponentHandlerWithEntity<C1, C2, C3, C4, C5, C6> handler, TRunner runner, RunnerBarrier? barrier)
        where TRunner : IRunner
        => host.Handle(handler,
            static (IEntityHost host, in ComponentHandlerWithEntity<C1, C2, C3, C4, C5, C6> handler, int from, int to) => {
                var entities = host.UnsafeGetEntitySpan();
                var desc = host.Descriptor;
                var c1Offset = desc.GetOffset<C1>();
                var c2Offset = desc.GetOffset<C2>();
                var c3Offset = desc.GetOffset<C3>();
                var c4Offset = desc.GetOffset<C4>();
                var c5Offset = desc.GetOffset<C5>();
                var c6Offset = desc.GetOffset<C6>();

                if (host is ISequentialEntityHost seqHost) {
                    ref byte memRef = ref seqHost.Bytes[0];
                    var size = seqHost.Descriptor.MemorySize;
                    for (int i = from; i != to; ++i) {
                        nint offset = i * size;
                        handler(entities[i],
                            ref c1Offset.Get(ref memRef, offset),
                            ref c2Offset.Get(ref memRef, offset),
                            ref c3Offset.Get(ref memRef, offset),
                            ref c4Offset.Get(ref memRef, offset),
                            ref c5Offset.Get(ref memRef, offset),
                            ref c6Offset.Get(ref memRef, offset));
                    }
                }
                else {
                    for (int i = from; i != to; ++i) {
                        ref var byteRef = ref host.GetByteRef(i);
                        handler(entities[i],
                            ref c1Offset.Get(ref byteRef),
                            ref c2Offset.Get(ref byteRef),
                            ref c3Offset.Get(ref byteRef),
                            ref c4Offset.Get(ref byteRef),
                            ref c5Offset.Get(ref byteRef),
                            ref c6Offset.Get(ref byteRef));
                    }
                }
            }, runner, barrier);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSlice<TRunner, TData, C1>(
        this IEntityHost host, in TData userData, DataComponentHandlerWithEntity<TData, C1> handler, TRunner runner, RunnerBarrier? barrier)
        where TRunner : IRunner
        => host.Handle((handler, userData),
            static (IEntityHost host, in (DataComponentHandlerWithEntity<TData, C1>, TData) data, int from, int to) => {
                var entities = host.UnsafeGetEntitySpan();
                var desc = host.Descriptor;
                var c1Offset = desc.GetOffset<C1>();

                var handler = data.Item1;
                ref readonly var userData = ref data.Item2;

                if (host is ISequentialEntityHost seqHost) {
                    ref byte memRef = ref seqHost.Bytes[0];
                    var size = seqHost.Descriptor.MemorySize;
                    for (int i = from; i != to; ++i) {
                        nint offset = i * size;
                        handler(entities[i], userData,
                            ref c1Offset.Get(ref memRef, offset));
                    }
                }
                else {
                    for (int i = from; i != to; ++i) {
                        ref var byteRef = ref host.GetByteRef(i);
                        handler(entities[i], userData, ref c1Offset.Get(ref byteRef));
                    }
                }
            }, runner, barrier);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSlice<TRunner, TData, C1, C2>(
        this IEntityHost host, in TData userData, DataComponentHandlerWithEntity<TData, C1, C2> handler, TRunner runner, RunnerBarrier? barrier)
        where TRunner : IRunner
        => host.Handle((handler, userData),
            static (IEntityHost host, in (DataComponentHandlerWithEntity<TData, C1, C2>, TData) data, int from, int to) => {
                var entities = host.UnsafeGetEntitySpan();
                var desc = host.Descriptor;
                var c1Offset = desc.GetOffset<C1>();
                var c2Offset = desc.GetOffset<C2>();

                var handler = data.Item1;
                ref readonly var userData = ref data.Item2;

                if (host is ISequentialEntityHost seqHost) {
                    ref byte memRef = ref seqHost.Bytes[0];
                    var size = seqHost.Descriptor.MemorySize;
                    for (int i = from; i != to; ++i) {
                        nint offset = i * size;
                        handler(entities[i], userData,
                            ref c1Offset.Get(ref memRef, offset),
                            ref c2Offset.Get(ref memRef, offset));
                    }
                }
                else {
                    for (int i = from; i != to; ++i) {
                        ref var byteRef = ref host.GetByteRef(i);
                        handler(entities[i], userData,
                            ref c1Offset.Get(ref byteRef),
                            ref c2Offset.Get(ref byteRef));
                    }
                }
            }, runner, barrier);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSlice<TRunner, TData, C1, C2, C3>(
        this IEntityHost host, in TData userData, DataComponentHandlerWithEntity<TData, C1, C2, C3> handler, TRunner runner, RunnerBarrier? barrier)
        where TRunner : IRunner
        => host.Handle((handler, userData),
            static (IEntityHost host, in (DataComponentHandlerWithEntity<TData, C1, C2, C3>, TData) data, int from, int to) => {
                var entities = host.UnsafeGetEntitySpan();
                var desc = host.Descriptor;
                var c1Offset = desc.GetOffset<C1>();
                var c2Offset = desc.GetOffset<C2>();
                var c3Offset = desc.GetOffset<C3>();

                var handler = data.Item1;
                ref readonly var userData = ref data.Item2;

                if (host is ISequentialEntityHost seqHost) {
                    ref byte memRef = ref seqHost.Bytes[0];
                    var size = seqHost.Descriptor.MemorySize;
                    for (int i = from; i != to; ++i) {
                        nint offset = i * size;
                        handler(entities[i], userData,
                            ref c1Offset.Get(ref memRef, offset),
                            ref c2Offset.Get(ref memRef, offset),
                            ref c3Offset.Get(ref memRef, offset));
                    }
                }
                else {
                    for (int i = from; i != to; ++i) {
                        ref var byteRef = ref host.GetByteRef(i);
                        handler(entities[i], userData,
                            ref c1Offset.Get(ref byteRef),
                            ref c2Offset.Get(ref byteRef),
                            ref c3Offset.Get(ref byteRef));
                    }
                }
            }, runner, barrier);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSlice<TRunner, TData, C1, C2, C3, C4>(
        this IEntityHost host, in TData userData, DataComponentHandlerWithEntity<TData, C1, C2, C3, C4> handler, TRunner runner, RunnerBarrier? barrier)
        where TRunner : IRunner
        => host.Handle((handler, userData),
            static (IEntityHost host, in (DataComponentHandlerWithEntity<TData, C1, C2, C3, C4>, TData) data, int from, int to) => {
                var entities = host.UnsafeGetEntitySpan();
                var desc = host.Descriptor;
                var c1Offset = desc.GetOffset<C1>();
                var c2Offset = desc.GetOffset<C2>();
                var c3Offset = desc.GetOffset<C3>();
                var c4Offset = desc.GetOffset<C4>();

                var handler = data.Item1;
                ref readonly var userData = ref data.Item2;

                if (host is ISequentialEntityHost seqHost) {
                    ref byte memRef = ref seqHost.Bytes[0];
                    var size = seqHost.Descriptor.MemorySize;
                    for (int i = from; i != to; ++i) {
                        nint offset = i * size;
                        handler(entities[i], userData,
                            ref c1Offset.Get(ref memRef, offset),
                            ref c2Offset.Get(ref memRef, offset),
                            ref c3Offset.Get(ref memRef, offset),
                            ref c4Offset.Get(ref memRef, offset));
                    }
                }
                else {
                    for (int i = from; i != to; ++i) {
                        ref var byteRef = ref host.GetByteRef(i);
                        handler(entities[i], userData,
                            ref c1Offset.Get(ref byteRef),
                            ref c2Offset.Get(ref byteRef),
                            ref c3Offset.Get(ref byteRef),
                            ref c4Offset.Get(ref byteRef));
                    }
                }
            }, runner, barrier);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSlice<TRunner, TData, C1, C2, C3, C4, C5>(
        this IEntityHost host, in TData userData, DataComponentHandlerWithEntity<TData, C1, C2, C3, C4, C5> handler, TRunner runner, RunnerBarrier? barrier)
        where TRunner : IRunner
        => host.Handle((handler, userData),
            static (IEntityHost host, in (DataComponentHandlerWithEntity<TData, C1, C2, C3, C4, C5>, TData) data, int from, int to) => {
                var entities = host.UnsafeGetEntitySpan();
                var desc = host.Descriptor;
                var c1Offset = desc.GetOffset<C1>();
                var c2Offset = desc.GetOffset<C2>();
                var c3Offset = desc.GetOffset<C3>();
                var c4Offset = desc.GetOffset<C4>();
                var c5Offset = desc.GetOffset<C5>();

                var handler = data.Item1;
                ref readonly var userData = ref data.Item2;

                if (host is ISequentialEntityHost seqHost) {
                    ref byte memRef = ref seqHost.Bytes[0];
                    var size = seqHost.Descriptor.MemorySize;
                    for (int i = from; i != to; ++i) {
                        nint offset = i * size;
                        handler(entities[i], userData,
                            ref c1Offset.Get(ref memRef, offset),
                            ref c2Offset.Get(ref memRef, offset),
                            ref c3Offset.Get(ref memRef, offset),
                            ref c4Offset.Get(ref memRef, offset),
                            ref c5Offset.Get(ref memRef, offset));
                    }
                }
                else {
                    for (int i = from; i != to; ++i) {
                        ref var byteRef = ref host.GetByteRef(i);
                        handler(entities[i], userData,
                            ref c1Offset.Get(ref byteRef),
                            ref c2Offset.Get(ref byteRef),
                            ref c3Offset.Get(ref byteRef),
                            ref c4Offset.Get(ref byteRef),
                            ref c5Offset.Get(ref byteRef));
                    }
                }
            }, runner, barrier);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSlice<TRunner, TData, C1, C2, C3, C4, C5, C6>(
        this IEntityHost host, in TData userData, DataComponentHandlerWithEntity<TData, C1, C2, C3, C4, C5, C6> handler, TRunner runner, RunnerBarrier? barrier)
        where TRunner : IRunner
        => host.Handle((handler, userData),
            static (IEntityHost host, in (DataComponentHandlerWithEntity<TData, C1, C2, C3, C4, C5, C6>, TData) data, int from, int to) => {
                var entities = host.UnsafeGetEntitySpan();
                var desc = host.Descriptor;
                var c1Offset = desc.GetOffset<C1>();
                var c2Offset = desc.GetOffset<C2>();
                var c3Offset = desc.GetOffset<C3>();
                var c4Offset = desc.GetOffset<C4>();
                var c5Offset = desc.GetOffset<C5>();
                var c6Offset = desc.GetOffset<C6>();

                var handler = data.Item1;
                ref readonly var userData = ref data.Item2;

                if (host is ISequentialEntityHost seqHost) {
                    ref byte memRef = ref seqHost.Bytes[0];
                    var size = seqHost.Descriptor.MemorySize;
                    for (int i = from; i != to; ++i) {
                        nint offset = i * size;
                        handler(entities[i], userData,
                            ref c1Offset.Get(ref memRef, offset),
                            ref c2Offset.Get(ref memRef, offset),
                            ref c3Offset.Get(ref memRef, offset),
                            ref c4Offset.Get(ref memRef, offset),
                            ref c5Offset.Get(ref memRef, offset),
                            ref c6Offset.Get(ref memRef, offset));
                    }
                }
                else {
                    for (int i = from; i != to; ++i) {
                        ref var byteRef = ref host.GetByteRef(i);
                        handler(entities[i], userData,
                            ref c1Offset.Get(ref byteRef),
                            ref c2Offset.Get(ref byteRef),
                            ref c3Offset.Get(ref byteRef),
                            ref c4Offset.Get(ref byteRef),
                            ref c5Offset.Get(ref byteRef),
                            ref c6Offset.Get(ref byteRef));
                    }
                }
            }, runner, barrier);

    #region CurrentThreadRunner

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSlice<C1>(
        this IEntityHost host, ComponentHandlerWithEntity<C1> handler)
        => host.ForSlice(handler, CurrentThreadRunner.Instance, barrier: null);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSlice<C1, C2>(
        this IEntityHost host, ComponentHandlerWithEntity<C1, C2> handler)
        => host.ForSlice(handler, CurrentThreadRunner.Instance, barrier: null);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSlice<C1, C2, C3>(
        this IEntityHost host, ComponentHandlerWithEntity<C1, C2, C3> handler)
        => host.ForSlice(handler, CurrentThreadRunner.Instance, barrier: null);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSlice<C1, C2, C3, C4>(
        this IEntityHost host, ComponentHandlerWithEntity<C1, C2, C3, C4> handler)
        => host.ForSlice(handler, CurrentThreadRunner.Instance, barrier: null);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSlice<C1, C2, C3, C4, C5>(
        this IEntityHost host, ComponentHandlerWithEntity<C1, C2, C3, C4, C5> handler)
        => host.ForSlice(handler, CurrentThreadRunner.Instance, barrier: null);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSlice<C1, C2, C3, C4, C5, C6>(
        this IEntityHost host, ComponentHandlerWithEntity<C1, C2, C3, C4, C5, C6> handler)
        => host.ForSlice(handler, CurrentThreadRunner.Instance, barrier: null);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSlice<TData, C1>(
        this IEntityHost host, in TData userData, DataComponentHandlerWithEntity<TData, C1> handler)
        => host.ForSlice(userData, handler, CurrentThreadRunner.Instance, barrier: null);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSlice<TData, C1, C2>(
        this IEntityHost host, in TData userData, DataComponentHandlerWithEntity<TData, C1, C2> handler)
        => host.ForSlice(userData, handler, CurrentThreadRunner.Instance, barrier: null);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSlice<TData, C1, C2, C3>(
        this IEntityHost host, in TData userData, DataComponentHandlerWithEntity<TData, C1, C2, C3> handler)
        => host.ForSlice(userData, handler, CurrentThreadRunner.Instance, barrier: null);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSlice<TData, C1, C2, C3, C4>(
        this IEntityHost host, in TData userData, DataComponentHandlerWithEntity<TData, C1, C2, C3, C4> handler)
        => host.ForSlice(userData, handler, CurrentThreadRunner.Instance, barrier: null);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSlice<TData, C1, C2, C3, C4, C5>(
        this IEntityHost host, in TData userData, DataComponentHandlerWithEntity<TData, C1, C2, C3, C4, C5> handler)
        => host.ForSlice(userData, handler, CurrentThreadRunner.Instance, barrier: null);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSlice<TData, C1, C2, C3, C4, C5, C6>(
        this IEntityHost host, in TData userData, DataComponentHandlerWithEntity<TData, C1, C2, C3, C4, C5, C6> handler)
        => host.ForSlice(userData, handler, CurrentThreadRunner.Instance, barrier: null);
    
    #endregion // CurrentThreadRunner

    #region ParallelRunner

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSliceOnParallel<C1>(
        this IEntityHost host, ComponentHandlerWithEntity<C1> handler)
    {
        var barrier = RunnerBarrier.Get();
        host.ForSlice(handler, ParallelRunner.Default, barrier);
        barrier.WaitAndReturn();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSliceOnParallel<C1, C2>(
        this IEntityHost host, ComponentHandlerWithEntity<C1, C2> handler)
    {
        var barrier = RunnerBarrier.Get();
        host.ForSlice(handler, ParallelRunner.Default, barrier);
        barrier.WaitAndReturn();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSliceOnParallel<C1, C2, C3>(
        this IEntityHost host, ComponentHandlerWithEntity<C1, C2, C3> handler)
    {
        var barrier = RunnerBarrier.Get();
        host.ForSlice(handler, ParallelRunner.Default, barrier);
        barrier.WaitAndReturn();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSliceOnParallel<C1, C2, C3, C4>(
        this IEntityHost host, ComponentHandlerWithEntity<C1, C2, C3, C4> handler)
    {
        var barrier = RunnerBarrier.Get();
        host.ForSlice(handler, ParallelRunner.Default, barrier);
        barrier.WaitAndReturn();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSliceOnParallel<C1, C2, C3, C4, C5>(
        this IEntityHost host, ComponentHandlerWithEntity<C1, C2, C3, C4, C5> handler)
    {
        var barrier = RunnerBarrier.Get();
        host.ForSlice(handler, ParallelRunner.Default, barrier);
        barrier.WaitAndReturn();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSliceOnParallel<C1, C2, C3, C4, C5, C6>(
        this IEntityHost host, ComponentHandlerWithEntity<C1, C2, C3, C4, C5, C6> handler)
    {
        var barrier = RunnerBarrier.Get();
        host.ForSlice(handler, ParallelRunner.Default, barrier);
        barrier.WaitAndReturn();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSliceOnParallel<TData, C1>(
        this IEntityHost host, in TData userData, DataComponentHandlerWithEntity<TData, C1> handler)
    {
        var barrier = RunnerBarrier.Get();
        host.ForSlice(userData, handler, ParallelRunner.Default, barrier);
        barrier.WaitAndReturn();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSliceOnParallel<TData, C1, C2>(
        this IEntityHost host, in TData userData, DataComponentHandlerWithEntity<TData, C1, C2> handler)
    {
        var barrier = RunnerBarrier.Get();
        host.ForSlice(userData, handler, ParallelRunner.Default, barrier);
        barrier.WaitAndReturn();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSliceOnParallel<TData, C1, C2, C3>(
        this IEntityHost host, in TData userData, DataComponentHandlerWithEntity<TData, C1, C2, C3> handler)
    {
        var barrier = RunnerBarrier.Get();
        host.ForSlice(userData, handler, ParallelRunner.Default, barrier);
        barrier.WaitAndReturn();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSliceOnParallel<TData, C1, C2, C3, C4>(
        this IEntityHost host, in TData userData, DataComponentHandlerWithEntity<TData, C1, C2, C3, C4> handler)
    {
        var barrier = RunnerBarrier.Get();
        host.ForSlice(userData, handler, ParallelRunner.Default, barrier);
        barrier.WaitAndReturn();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSliceOnParallel<TData, C1, C2, C3, C4, C5>(
        this IEntityHost host, in TData userData, DataComponentHandlerWithEntity<TData, C1, C2, C3, C4, C5> handler)
    {
        var barrier = RunnerBarrier.Get();
        host.ForSlice(userData, handler, ParallelRunner.Default, barrier);
        barrier.WaitAndReturn();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSliceOnParallel<TData, C1, C2, C3, C4, C5, C6>(
        this IEntityHost host, in TData userData, DataComponentHandlerWithEntity<TData, C1, C2, C3, C4, C5, C6> handler)
    {
        var barrier = RunnerBarrier.Get();
        host.ForSlice(userData, handler, ParallelRunner.Default, barrier);
        barrier.WaitAndReturn();
    }
    
    #endregion // ParallelRunner

    #endregion // ForSlice_WithEntity
}