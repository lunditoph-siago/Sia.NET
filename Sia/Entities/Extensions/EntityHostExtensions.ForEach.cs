namespace Sia;

using System.Runtime.CompilerServices;

public static partial class EntityHostExtensions
{
    #region EntityHandler

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void ForEach<TRunner>(
        this IEntityHost host, EntityHandler handler, TRunner runner)
        where TRunner : IRunner
        => host.Handle(handler,
            static (IEntityHost host, in EntityHandler handler, int from, int to) => {
                var slosts = host.AllocatedSlots;
                for (int i = from; i != to; ++i) {
                    handler(new(slosts[i], host));
                }
            }, runner);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void ForEach<TRunner, TData>(
        this IEntityHost host, in TData userData, EntityHandler<TData> handler, TRunner runner)
        where TRunner : IRunner
        => host.Handle((userData, handler),
            static (IEntityHost host, in (TData, EntityHandler<TData>) entry, int from, int to) => {
                ref readonly var data = ref entry.Item1;
                var handler = entry.Item2;
                var slosts = host.AllocatedSlots;
                for (int i = from; i != to; ++i) {
                    handler(data, new(slosts[i], host));
                }
            }, runner);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void ForEach<TRunner>(
        this IEntityHost host, SimpleEntityHandler handler, TRunner runner)
        where TRunner : IRunner
        => host.ForEach(handler,
            static (in SimpleEntityHandler handler, in EntityRef entity)
                => handler(entity), runner);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void ForEach<TRunner, TData>(
        this IEntityHost host, in TData userData, SimpleEntityHandler<TData> handler, TRunner runner)
        where TRunner : IRunner
        => host.ForEach((handler, userData),
            static (in (SimpleEntityHandler<TData>, TData) data, in EntityRef entity)
                => data.Item1(data.Item2, entity), runner);

    #region CurrentThreadRunner

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void ForEach(this IEntityHost host, EntityHandler handler)
        => host.ForEach(handler, CurrentThreadRunner.Instance);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void ForEach<TData>(
        this IEntityHost host, in TData data, EntityHandler<TData> handler)
        => host.ForEach(data, handler, CurrentThreadRunner.Instance);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void ForEach(this IEntityHost host, SimpleEntityHandler handler)
        => host.ForEach(handler, CurrentThreadRunner.Instance);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void ForEach<TData>(
        this IEntityHost host, in TData data, SimpleEntityHandler<TData> handler)
        => host.ForEach(data, handler, CurrentThreadRunner.Instance);
    
    #endregion // CurrentThreadRunner

   #region ParallelRunner

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void ForEachOnParallel(this IEntityHost host, EntityHandler handler)
        => host.ForEach(handler, ParallelRunner.Default);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void ForEachOnParallel<TData>(
        this IEntityHost host, in TData data, EntityHandler<TData> handler)
        => host.ForEach(data, handler, ParallelRunner.Default);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void ForEachOnParallel(this IEntityHost host, SimpleEntityHandler handler)
        => host.ForEach(handler, ParallelRunner.Default);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void ForEachOnParallel<TData>(
        this IEntityHost host, in TData data, SimpleEntityHandler<TData> handler)
        => host.ForEach(data, handler, ParallelRunner.Default);
    
    #endregion // ParallelRunner

    #endregion // EntityHandler

    #region ComponentHanlder

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSlice<TRunner, C1>(
        this IEntityHost host, ComponentHandler<C1> handler, TRunner runner)
        where TRunner : IRunner
        => host.Handle(handler,
            static (IEntityHost host, in ComponentHandler<C1> handler, int from, int to) => {
                var desc = host.Descriptor;
                var slots = host.AllocatedSlots;

                var c1Offset = desc.GetOffset<C1>();

                for (int i = from; i != to; ++i) {
                    ref var byteRef = ref host.UnsafeGetByteRef(slots[i]);
                    handler(ref Unsafe.As<byte, C1>(ref Unsafe.AddByteOffset(ref byteRef, c1Offset)));
                }
            }, runner);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSlice<TRunner, C1, C2>(
        this IEntityHost host, ComponentHandler<C1, C2> handler, TRunner runner)
        where TRunner : IRunner
        => host.Handle(handler,
            static (IEntityHost host, in ComponentHandler<C1, C2> handler, int from, int to) => {
                var desc = host.Descriptor;
                var slots = host.AllocatedSlots;

                var c1Offset = desc.GetOffset<C1>();
                var c2Offset = desc.GetOffset<C2>();

                for (int i = from; i != to; ++i) {
                    ref var byteRef = ref host.UnsafeGetByteRef(slots[i]);
                    handler(
                        ref Unsafe.As<byte, C1>(ref Unsafe.AddByteOffset(ref byteRef, c1Offset)),
                        ref Unsafe.As<byte, C2>(ref Unsafe.AddByteOffset(ref byteRef, c2Offset)));
                }
            }, runner);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSlice<TRunner, C1, C2, C3>(
        this IEntityHost host, ComponentHandler<C1, C2, C3> handler, TRunner runner)
        where TRunner : IRunner
        => host.Handle(handler,
            static (IEntityHost host, in ComponentHandler<C1, C2, C3> handler, int from, int to) => {
                var desc = host.Descriptor;
                var slots = host.AllocatedSlots;

                var c1Offset = desc.GetOffset<C1>();
                var c2Offset = desc.GetOffset<C2>();
                var c3Offset = desc.GetOffset<C3>();

                for (int i = from; i != to; ++i) {
                    ref var byteRef = ref host.UnsafeGetByteRef(slots[i]);
                    handler(
                        ref Unsafe.As<byte, C1>(ref Unsafe.AddByteOffset(ref byteRef, c1Offset)),
                        ref Unsafe.As<byte, C2>(ref Unsafe.AddByteOffset(ref byteRef, c2Offset)),
                        ref Unsafe.As<byte, C3>(ref Unsafe.AddByteOffset(ref byteRef, c3Offset)));
                }
            }, runner);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSlice<TRunner, C1, C2, C3, C4>(
        this IEntityHost host, ComponentHandler<C1, C2, C3, C4> handler, TRunner runner)
        where TRunner : IRunner
        => host.Handle(handler,
            static (IEntityHost host, in ComponentHandler<C1, C2, C3, C4> handler, int from, int to) => {
                var desc = host.Descriptor;
                var slots = host.AllocatedSlots;

                var c1Offset = desc.GetOffset<C1>();
                var c2Offset = desc.GetOffset<C2>();
                var c3Offset = desc.GetOffset<C3>();
                var c4Offset = desc.GetOffset<C4>();

                for (int i = from; i != to; ++i) {
                    ref var byteRef = ref host.UnsafeGetByteRef(slots[i]);
                    handler(
                        ref Unsafe.As<byte, C1>(ref Unsafe.AddByteOffset(ref byteRef, c1Offset)),
                        ref Unsafe.As<byte, C2>(ref Unsafe.AddByteOffset(ref byteRef, c2Offset)),
                        ref Unsafe.As<byte, C3>(ref Unsafe.AddByteOffset(ref byteRef, c3Offset)),
                        ref Unsafe.As<byte, C4>(ref Unsafe.AddByteOffset(ref byteRef, c4Offset)));
                }
            }, runner);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSlice<TRunner, C1, C2, C3, C4, C5>(
        this IEntityHost host, ComponentHandler<C1, C2, C3, C4, C5> handler, TRunner runner)
        where TRunner : IRunner
        => host.Handle(handler,
            static (IEntityHost host, in ComponentHandler<C1, C2, C3, C4, C5> handler, int from, int to) => {
                var desc = host.Descriptor;
                var slots = host.AllocatedSlots;

                var c1Offset = desc.GetOffset<C1>();
                var c2Offset = desc.GetOffset<C2>();
                var c3Offset = desc.GetOffset<C3>();
                var c4Offset = desc.GetOffset<C4>();
                var c5Offset = desc.GetOffset<C5>();

                for (int i = from; i != to; ++i) {
                    ref var byteRef = ref host.UnsafeGetByteRef(slots[i]);
                    handler(
                        ref Unsafe.As<byte, C1>(ref Unsafe.AddByteOffset(ref byteRef, c1Offset)),
                        ref Unsafe.As<byte, C2>(ref Unsafe.AddByteOffset(ref byteRef, c2Offset)),
                        ref Unsafe.As<byte, C3>(ref Unsafe.AddByteOffset(ref byteRef, c3Offset)),
                        ref Unsafe.As<byte, C4>(ref Unsafe.AddByteOffset(ref byteRef, c4Offset)),
                        ref Unsafe.As<byte, C5>(ref Unsafe.AddByteOffset(ref byteRef, c5Offset)));
                }
            }, runner);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSlice<TRunner, C1, C2, C3, C4, C5, C6>(
        this IEntityHost host, ComponentHandler<C1, C2, C3, C4, C5, C6> handler, TRunner runner)
        where TRunner : IRunner
        => host.Handle(handler,
            static (IEntityHost host, in ComponentHandler<C1, C2, C3, C4, C5, C6> handler, int from, int to) => {
                var desc = host.Descriptor;
                var slots = host.AllocatedSlots;

                var c1Offset = desc.GetOffset<C1>();
                var c2Offset = desc.GetOffset<C2>();
                var c3Offset = desc.GetOffset<C3>();
                var c4Offset = desc.GetOffset<C4>();
                var c5Offset = desc.GetOffset<C5>();
                var c6Offset = desc.GetOffset<C6>();

                for (int i = from; i != to; ++i) {
                    ref var byteRef = ref host.UnsafeGetByteRef(slots[i]);
                    handler(
                        ref Unsafe.As<byte, C1>(ref Unsafe.AddByteOffset(ref byteRef, c1Offset)),
                        ref Unsafe.As<byte, C2>(ref Unsafe.AddByteOffset(ref byteRef, c2Offset)),
                        ref Unsafe.As<byte, C3>(ref Unsafe.AddByteOffset(ref byteRef, c3Offset)),
                        ref Unsafe.As<byte, C4>(ref Unsafe.AddByteOffset(ref byteRef, c4Offset)),
                        ref Unsafe.As<byte, C5>(ref Unsafe.AddByteOffset(ref byteRef, c5Offset)),
                        ref Unsafe.As<byte, C6>(ref Unsafe.AddByteOffset(ref byteRef, c6Offset)));
                }
            }, runner);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSlice<TRunner, TData, C1>(
        this IEntityHost host, in TData userData, DataComponentHandler<TData, C1> handler, TRunner runner)
        where TRunner : IRunner
        => host.Handle((handler, userData),
            static (IEntityHost host, in (DataComponentHandler<TData, C1>, TData) data, int from, int to) => {
                var desc = host.Descriptor;
                var slots = host.AllocatedSlots;

                var c1Offset = desc.GetOffset<C1>();

                var handler = data.Item1;
                ref readonly var userData = ref data.Item2;

                for (int i = from; i != to; ++i) {
                    ref var byteRef = ref host.UnsafeGetByteRef(slots[i]);
                    handler(userData, ref Unsafe.As<byte, C1>(ref Unsafe.AddByteOffset(ref byteRef, c1Offset)));
                }
            }, runner);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSlice<TRunner, TData, C1, C2>(
        this IEntityHost host, in TData userData, DataComponentHandler<TData, C1, C2> handler, TRunner runner)
        where TRunner : IRunner
        => host.Handle((handler, userData),
            static (IEntityHost host, in (DataComponentHandler<TData, C1, C2>, TData) data, int from, int to) => {
                var desc = host.Descriptor;
                var slots = host.AllocatedSlots;

                var c1Offset = desc.GetOffset<C1>();
                var c2Offset = desc.GetOffset<C2>();

                var handler = data.Item1;
                ref readonly var userData = ref data.Item2;

                for (int i = from; i != to; ++i) {
                    ref var byteRef = ref host.UnsafeGetByteRef(slots[i]);
                    handler(userData,
                        ref Unsafe.As<byte, C1>(ref Unsafe.AddByteOffset(ref byteRef, c1Offset)),
                        ref Unsafe.As<byte, C2>(ref Unsafe.AddByteOffset(ref byteRef, c2Offset)));
                }
            }, runner);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSlice<TRunner, TData, C1, C2, C3>(
        this IEntityHost host, in TData userData, DataComponentHandler<TData, C1, C2, C3> handler, TRunner runner)
        where TRunner : IRunner
        => host.Handle((handler, userData),
            static (IEntityHost host, in (DataComponentHandler<TData, C1, C2, C3>, TData) data, int from, int to) => {
                var desc = host.Descriptor;
                var slots = host.AllocatedSlots;

                var c1Offset = desc.GetOffset<C1>();
                var c2Offset = desc.GetOffset<C2>();
                var c3Offset = desc.GetOffset<C3>();

                var handler = data.Item1;
                ref readonly var userData = ref data.Item2;

                for (int i = from; i != to; ++i) {
                    ref var byteRef = ref host.UnsafeGetByteRef(slots[i]);
                    handler(userData,
                        ref Unsafe.As<byte, C1>(ref Unsafe.AddByteOffset(ref byteRef, c1Offset)),
                        ref Unsafe.As<byte, C2>(ref Unsafe.AddByteOffset(ref byteRef, c2Offset)),
                        ref Unsafe.As<byte, C3>(ref Unsafe.AddByteOffset(ref byteRef, c3Offset)));
                }
            }, runner);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSlice<TRunner, TData, C1, C2, C3, C4>(
        this IEntityHost host, in TData userData, DataComponentHandler<TData, C1, C2, C3, C4> handler, TRunner runner)
        where TRunner : IRunner
        => host.Handle((handler, userData),
            static (IEntityHost host, in (DataComponentHandler<TData, C1, C2, C3, C4>, TData) data, int from, int to) => {
                var desc = host.Descriptor;
                var slots = host.AllocatedSlots;

                var c1Offset = desc.GetOffset<C1>();
                var c2Offset = desc.GetOffset<C2>();
                var c3Offset = desc.GetOffset<C3>();
                var c4Offset = desc.GetOffset<C4>();

                var handler = data.Item1;
                ref readonly var userData = ref data.Item2;

                for (int i = from; i != to; ++i) {
                    ref var byteRef = ref host.UnsafeGetByteRef(slots[i]);
                    handler(userData,
                        ref Unsafe.As<byte, C1>(ref Unsafe.AddByteOffset(ref byteRef, c1Offset)),
                        ref Unsafe.As<byte, C2>(ref Unsafe.AddByteOffset(ref byteRef, c2Offset)),
                        ref Unsafe.As<byte, C3>(ref Unsafe.AddByteOffset(ref byteRef, c3Offset)),
                        ref Unsafe.As<byte, C4>(ref Unsafe.AddByteOffset(ref byteRef, c4Offset)));
                }
            }, runner);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSlice<TRunner, TData, C1, C2, C3, C4, C5>(
        this IEntityHost host, in TData userData, DataComponentHandler<TData, C1, C2, C3, C4, C5> handler, TRunner runner)
        where TRunner : IRunner
        => host.Handle((handler, userData),
            static (IEntityHost host, in (DataComponentHandler<TData, C1, C2, C3, C4, C5>, TData) data, int from, int to) => {
                var desc = host.Descriptor;
                var slots = host.AllocatedSlots;

                var c1Offset = desc.GetOffset<C1>();
                var c2Offset = desc.GetOffset<C2>();
                var c3Offset = desc.GetOffset<C3>();
                var c4Offset = desc.GetOffset<C4>();
                var c5Offset = desc.GetOffset<C5>();

                var handler = data.Item1;
                ref readonly var userData = ref data.Item2;

                for (int i = from; i != to; ++i) {
                    ref var byteRef = ref host.UnsafeGetByteRef(slots[i]);
                    handler(userData,
                        ref Unsafe.As<byte, C1>(ref Unsafe.AddByteOffset(ref byteRef, c1Offset)),
                        ref Unsafe.As<byte, C2>(ref Unsafe.AddByteOffset(ref byteRef, c2Offset)),
                        ref Unsafe.As<byte, C3>(ref Unsafe.AddByteOffset(ref byteRef, c3Offset)),
                        ref Unsafe.As<byte, C4>(ref Unsafe.AddByteOffset(ref byteRef, c4Offset)),
                        ref Unsafe.As<byte, C5>(ref Unsafe.AddByteOffset(ref byteRef, c5Offset)));
                }
            }, runner);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSlice<TRunner, TData, C1, C2, C3, C4, C5, C6>(
        this IEntityHost host, in TData userData, DataComponentHandler<TData, C1, C2, C3, C4, C5, C6> handler, TRunner runner)
        where TRunner : IRunner
        => host.Handle((handler, userData),
            static (IEntityHost host, in (DataComponentHandler<TData, C1, C2, C3, C4, C5, C6>, TData) data, int from, int to) => {
                var desc = host.Descriptor;
                var slots = host.AllocatedSlots;

                var c1Offset = desc.GetOffset<C1>();
                var c2Offset = desc.GetOffset<C2>();
                var c3Offset = desc.GetOffset<C3>();
                var c4Offset = desc.GetOffset<C4>();
                var c5Offset = desc.GetOffset<C5>();
                var c6Offset = desc.GetOffset<C6>();

                var handler = data.Item1;
                ref readonly var userData = ref data.Item2;

                for (int i = from; i != to; ++i) {
                    ref var byteRef = ref host.UnsafeGetByteRef(slots[i]);
                    handler(userData,
                        ref Unsafe.As<byte, C1>(ref Unsafe.AddByteOffset(ref byteRef, c1Offset)),
                        ref Unsafe.As<byte, C2>(ref Unsafe.AddByteOffset(ref byteRef, c2Offset)),
                        ref Unsafe.As<byte, C3>(ref Unsafe.AddByteOffset(ref byteRef, c3Offset)),
                        ref Unsafe.As<byte, C4>(ref Unsafe.AddByteOffset(ref byteRef, c4Offset)),
                        ref Unsafe.As<byte, C5>(ref Unsafe.AddByteOffset(ref byteRef, c5Offset)),
                        ref Unsafe.As<byte, C6>(ref Unsafe.AddByteOffset(ref byteRef, c6Offset)));
                }
            }, runner);

    #region CurrentThreadRunner

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSlice<C1>(
        this IEntityHost host, ComponentHandler<C1> handler)
        => host.ForSlice(handler, CurrentThreadRunner.Instance);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSlice<C1, C2>(
        this IEntityHost host, ComponentHandler<C1, C2> handler)
        => host.ForSlice(handler, CurrentThreadRunner.Instance);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSlice<C1, C2, C3>(
        this IEntityHost host, ComponentHandler<C1, C2, C3> handler)
        => host.ForSlice(handler, CurrentThreadRunner.Instance);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSlice<C1, C2, C3, C4>(
        this IEntityHost host, ComponentHandler<C1, C2, C3, C4> handler)
        => host.ForSlice(handler, CurrentThreadRunner.Instance);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSlice<C1, C2, C3, C4, C5>(
        this IEntityHost host, ComponentHandler<C1, C2, C3, C4, C5> handler)
        => host.ForSlice(handler, CurrentThreadRunner.Instance);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSlice<C1, C2, C3, C4, C5, C6>(
        this IEntityHost host, ComponentHandler<C1, C2, C3, C4, C5, C6> handler)
        => host.ForSlice(handler, CurrentThreadRunner.Instance);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSlice<TData, C1>(
        this IEntityHost host, in TData userData, DataComponentHandler<TData, C1> handler)
        => host.ForSlice(userData, handler, CurrentThreadRunner.Instance);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSlice<TData, C1, C2>(
        this IEntityHost host, in TData userData, DataComponentHandler<TData, C1, C2> handler)
        => host.ForSlice(userData, handler, CurrentThreadRunner.Instance);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSlice<TData, C1, C2, C3>(
        this IEntityHost host, in TData userData, DataComponentHandler<TData, C1, C2, C3> handler)
        => host.ForSlice(userData, handler, CurrentThreadRunner.Instance);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSlice<TData, C1, C2, C3, C4>(
        this IEntityHost host, in TData userData, DataComponentHandler<TData, C1, C2, C3, C4> handler)
        => host.ForSlice(userData, handler, CurrentThreadRunner.Instance);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSlice<TData, C1, C2, C3, C4, C5>(
        this IEntityHost host, in TData userData, DataComponentHandler<TData, C1, C2, C3, C4, C5> handler)
        => host.ForSlice(userData, handler, CurrentThreadRunner.Instance);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSlice<TData, C1, C2, C3, C4, C5, C6>(
        this IEntityHost host, in TData userData, DataComponentHandler<TData, C1, C2, C3, C4, C5, C6> handler)
        => host.ForSlice(userData, handler, CurrentThreadRunner.Instance);
    
    #endregion // ParallelRunner

    #region ParallelRunner

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSliceOnParallel<C1>(
        this IEntityHost host, ComponentHandler<C1> handler)
        => host.ForSlice(handler, ParallelRunner.Default);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSliceOnParallel<C1, C2>(
        this IEntityHost host, ComponentHandler<C1, C2> handler)
        => host.ForSlice(handler, ParallelRunner.Default);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSliceOnParallel<C1, C2, C3>(
        this IEntityHost host, ComponentHandler<C1, C2, C3> handler)
        => host.ForSlice(handler, ParallelRunner.Default);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSliceOnParallel<C1, C2, C3, C4>(
        this IEntityHost host, ComponentHandler<C1, C2, C3, C4> handler)
        => host.ForSlice(handler, ParallelRunner.Default);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSliceOnParallel<C1, C2, C3, C4, C5>(
        this IEntityHost host, ComponentHandler<C1, C2, C3, C4, C5> handler)
        => host.ForSlice(handler, ParallelRunner.Default);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSliceOnParallel<C1, C2, C3, C4, C5, C6>(
        this IEntityHost host, ComponentHandler<C1, C2, C3, C4, C5, C6> handler)
        => host.ForSlice(handler, ParallelRunner.Default);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSliceOnParallel<TData, C1>(
        this IEntityHost host, in TData userData, DataComponentHandler<TData, C1> handler)
        => host.ForSlice(userData, handler, ParallelRunner.Default);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSliceOnParallel<TData, C1, C2>(
        this IEntityHost host, in TData userData, DataComponentHandler<TData, C1, C2> handler)
        => host.ForSlice(userData, handler, ParallelRunner.Default);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSliceOnParallel<TData, C1, C2, C3>(
        this IEntityHost host, in TData userData, DataComponentHandler<TData, C1, C2, C3> handler)
        => host.ForSlice(userData, handler, ParallelRunner.Default);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSliceOnParallel<TData, C1, C2, C3, C4>(
        this IEntityHost host, in TData userData, DataComponentHandler<TData, C1, C2, C3, C4> handler)
        => host.ForSlice(userData, handler, ParallelRunner.Default);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSliceOnParallel<TData, C1, C2, C3, C4, C5>(
        this IEntityHost host, in TData userData, DataComponentHandler<TData, C1, C2, C3, C4, C5> handler)
        => host.ForSlice(userData, handler, ParallelRunner.Default);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForSliceOnParallel<TData, C1, C2, C3, C4, C5, C6>(
        this IEntityHost host, in TData userData, DataComponentHandler<TData, C1, C2, C3, C4, C5, C6> handler)
        => host.ForSlice(userData, handler, ParallelRunner.Default);
    
    #endregion // ParallelRunner

    #endregion // ComponentHandler
}