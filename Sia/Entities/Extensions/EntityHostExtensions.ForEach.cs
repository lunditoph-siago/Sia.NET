namespace Sia;

using System.Runtime.CompilerServices;

public static partial class EntityHostExtensions
{
    #region EntityHandler

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ForEach<TRunner>(
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
    public static void ForEach<TRunner, TData>(
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
    public static void ForEach<TRunner>(
        this IEntityHost host, SimpleEntityHandler handler, TRunner runner)
        where TRunner : IRunner
        => host.ForEach(handler,
            static (in SimpleEntityHandler handler, in EntityRef entity)
                => handler(entity), runner);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ForEach<TRunner, TData>(
        this IEntityHost host, in TData userData, SimpleEntityHandler<TData> handler, TRunner runner)
        where TRunner : IRunner
        => host.ForEach((handler, userData),
            static (in (SimpleEntityHandler<TData>, TData) data, in EntityRef entity)
                => data.Item1(data.Item2, entity), runner);

    #region CurrentThreadRunner

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ForEach(this IEntityHost host, EntityHandler handler)
        => host.ForEach(handler, CurrentThreadRunner.Instance);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ForEach<TData>(
        this IEntityHost host, in TData data, EntityHandler<TData> handler)
        => host.ForEach(data, handler, CurrentThreadRunner.Instance);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ForEach(this IEntityHost host, SimpleEntityHandler handler)
        => host.ForEach(handler, CurrentThreadRunner.Instance);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ForEach<TData>(
        this IEntityHost host, in TData data, SimpleEntityHandler<TData> handler)
        => host.ForEach(data, handler, CurrentThreadRunner.Instance);
    
    #endregion // CurrentThreadRunner

   #region ParallelRunner

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ForEachOnParallel(this IEntityHost host, EntityHandler handler)
        => host.ForEach(handler, ParallelRunner.Default);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ForEachOnParallel<TData>(
        this IEntityHost host, in TData data, EntityHandler<TData> handler)
        => host.ForEach(data, handler, ParallelRunner.Default);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ForEachOnParallel(this IEntityHost host, SimpleEntityHandler handler)
        => host.ForEach(handler, ParallelRunner.Default);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ForEachOnParallel<TData>(
        this IEntityHost host, in TData data, SimpleEntityHandler<TData> handler)
        => host.ForEach(data, handler, ParallelRunner.Default);
    
    #endregion // ParallelRunner

    #endregion // EntityHandler

    #region ComponentHanlder

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ForSlice<TRunner, C1>(
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
    public static void ForSlice<TRunner, C1, C2>(
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
    public static void ForSlice<TRunner, C1, C2, C3>(
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
    public static void ForSlice<TRunner, C1, C2, C3, C4>(
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
    public static void ForSlice<TRunner, C1, C2, C3, C4, C5>(
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
    public static void ForSlice<TRunner, C1, C2, C3, C4, C5, C6>(
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
    public static void ForSlice<TRunner, TData, C1>(
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
    public static void ForSlice<TRunner, TData, C1, C2>(
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
    public static void ForSlice<TRunner, TData, C1, C2, C3>(
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
    public static void ForSlice<TRunner, TData, C1, C2, C3, C4>(
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
    public static void ForSlice<TRunner, TData, C1, C2, C3, C4, C5>(
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
    public static void ForSlice<TRunner, TData, C1, C2, C3, C4, C5, C6>(
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
    public static void ForSlice<C1>(
        this IEntityHost host, ComponentHandler<C1> handler)
        => host.ForSlice(handler, CurrentThreadRunner.Instance);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ForSlice<C1, C2>(
        this IEntityHost host, ComponentHandler<C1, C2> handler)
        => host.ForSlice(handler, CurrentThreadRunner.Instance);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ForSlice<C1, C2, C3>(
        this IEntityHost host, ComponentHandler<C1, C2, C3> handler)
        => host.ForSlice(handler, CurrentThreadRunner.Instance);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ForSlice<C1, C2, C3, C4>(
        this IEntityHost host, ComponentHandler<C1, C2, C3, C4> handler)
        => host.ForSlice(handler, CurrentThreadRunner.Instance);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ForSlice<C1, C2, C3, C4, C5>(
        this IEntityHost host, ComponentHandler<C1, C2, C3, C4, C5> handler)
        => host.ForSlice(handler, CurrentThreadRunner.Instance);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ForSlice<C1, C2, C3, C4, C5, C6>(
        this IEntityHost host, ComponentHandler<C1, C2, C3, C4, C5, C6> handler)
        => host.ForSlice(handler, CurrentThreadRunner.Instance);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ForSlice<TData, C1>(
        this IEntityHost host, in TData userData, DataComponentHandler<TData, C1> handler)
        => host.ForSlice(userData, handler, CurrentThreadRunner.Instance);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ForSlice<TData, C1, C2>(
        this IEntityHost host, in TData userData, DataComponentHandler<TData, C1, C2> handler)
        => host.ForSlice(userData, handler, CurrentThreadRunner.Instance);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ForSlice<TData, C1, C2, C3>(
        this IEntityHost host, in TData userData, DataComponentHandler<TData, C1, C2, C3> handler)
        => host.ForSlice(userData, handler, CurrentThreadRunner.Instance);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ForSlice<TData, C1, C2, C3, C4>(
        this IEntityHost host, in TData userData, DataComponentHandler<TData, C1, C2, C3, C4> handler)
        => host.ForSlice(userData, handler, CurrentThreadRunner.Instance);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ForSlice<TData, C1, C2, C3, C4, C5>(
        this IEntityHost host, in TData userData, DataComponentHandler<TData, C1, C2, C3, C4, C5> handler)
        => host.ForSlice(userData, handler, CurrentThreadRunner.Instance);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ForSlice<TData, C1, C2, C3, C4, C5, C6>(
        this IEntityHost host, in TData userData, DataComponentHandler<TData, C1, C2, C3, C4, C5, C6> handler)
        => host.ForSlice(userData, handler, CurrentThreadRunner.Instance);
    
    #endregion // ParallelRunner

    #region ParallelRunner

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ForSliceOnParallel<C1>(
        this IEntityHost host, ComponentHandler<C1> handler)
        => host.ForSlice(handler, ParallelRunner.Default);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ForSliceOnParallel<C1, C2>(
        this IEntityHost host, ComponentHandler<C1, C2> handler)
        => host.ForSlice(handler, ParallelRunner.Default);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ForSliceOnParallel<C1, C2, C3>(
        this IEntityHost host, ComponentHandler<C1, C2, C3> handler)
        => host.ForSlice(handler, ParallelRunner.Default);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ForSliceOnParallel<C1, C2, C3, C4>(
        this IEntityHost host, ComponentHandler<C1, C2, C3, C4> handler)
        => host.ForSlice(handler, ParallelRunner.Default);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ForSliceOnParallel<C1, C2, C3, C4, C5>(
        this IEntityHost host, ComponentHandler<C1, C2, C3, C4, C5> handler)
        => host.ForSlice(handler, ParallelRunner.Default);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ForSliceOnParallel<C1, C2, C3, C4, C5, C6>(
        this IEntityHost host, ComponentHandler<C1, C2, C3, C4, C5, C6> handler)
        => host.ForSlice(handler, ParallelRunner.Default);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ForSliceOnParallel<TData, C1>(
        this IEntityHost host, in TData userData, DataComponentHandler<TData, C1> handler)
        => host.ForSlice(userData, handler, ParallelRunner.Default);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ForSliceOnParallel<TData, C1, C2>(
        this IEntityHost host, in TData userData, DataComponentHandler<TData, C1, C2> handler)
        => host.ForSlice(userData, handler, ParallelRunner.Default);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ForSliceOnParallel<TData, C1, C2, C3>(
        this IEntityHost host, in TData userData, DataComponentHandler<TData, C1, C2, C3> handler)
        => host.ForSlice(userData, handler, ParallelRunner.Default);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ForSliceOnParallel<TData, C1, C2, C3, C4>(
        this IEntityHost host, in TData userData, DataComponentHandler<TData, C1, C2, C3, C4> handler)
        => host.ForSlice(userData, handler, ParallelRunner.Default);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ForSliceOnParallel<TData, C1, C2, C3, C4, C5>(
        this IEntityHost host, in TData userData, DataComponentHandler<TData, C1, C2, C3, C4, C5> handler)
        => host.ForSlice(userData, handler, ParallelRunner.Default);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ForSliceOnParallel<TData, C1, C2, C3, C4, C5, C6>(
        this IEntityHost host, in TData userData, DataComponentHandler<TData, C1, C2, C3, C4, C5, C6> handler)
        => host.ForSlice(userData, handler, ParallelRunner.Default);
    
    #endregion // ParallelRunner

    #endregion // ComponentHandler

    #region ComponentFilter

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Filter<TRunner, C1>(
        this IEntityHost host, ComponentFilter<C1> filter, EntityHandler handler, TRunner runner)
        where TRunner : IRunner
        => host.Handle((filter, handler),
            static (IEntityHost host, in (ComponentFilter<C1>, EntityHandler) fs, int from, int to) => {
                var desc = host.Descriptor;
                var slots = host.AllocatedSlots;
                var (filter, handler) = fs;

                var c1Offset = desc.GetOffset<C1>();

                for (int i = from; i != to; ++i) {
                    ref readonly var slot = ref slots[i];
                    ref var byteRef = ref host.UnsafeGetByteRef(slot);

                    if (filter(ref Unsafe.As<byte, C1>(ref Unsafe.AddByteOffset(ref byteRef, c1Offset)))) {
                        handler(new(slot, host));
                    }
                }
            }, runner);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Filter<TRunner, C1, C2>(
        this IEntityHost host, ComponentFilter<C1, C2> filter, EntityHandler handler, TRunner runner)
        where TRunner : IRunner
        => host.Handle((filter, handler),
            static (IEntityHost host, in (ComponentFilter<C1, C2>, EntityHandler) fs, int from, int to) => {
                var desc = host.Descriptor;
                var slots = host.AllocatedSlots;
                var (filter, handler) = fs;

                var c1Offset = desc.GetOffset<C1>();
                var c2Offset = desc.GetOffset<C2>();

                for (int i = from; i != to; ++i) {
                    ref readonly var slot = ref slots[i];
                    ref var byteRef = ref host.UnsafeGetByteRef(slot);

                    if (filter(
                        ref Unsafe.As<byte, C1>(ref Unsafe.AddByteOffset(ref byteRef, c1Offset)),
                        ref Unsafe.As<byte, C2>(ref Unsafe.AddByteOffset(ref byteRef, c2Offset)))) {
                        handler(new(slot, host));
                    }
                }
            }, runner);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Filter<TRunner, C1, C2, C3>(
        this IEntityHost host, ComponentFilter<C1, C2, C3> filter, EntityHandler handler, TRunner runner)
        where TRunner : IRunner
        => host.Handle((filter, handler),
            static (IEntityHost host, in (ComponentFilter<C1, C2, C3>, EntityHandler) fs, int from, int to) => {
                var desc = host.Descriptor;
                var slots = host.AllocatedSlots;
                var (filter, handler) = fs;

                var c1Offset = desc.GetOffset<C1>();
                var c2Offset = desc.GetOffset<C2>();
                var c3Offset = desc.GetOffset<C3>();

                for (int i = from; i != to; ++i) {
                    ref readonly var slot = ref slots[i];
                    ref var byteRef = ref host.UnsafeGetByteRef(slot);

                    if (filter(
                        ref Unsafe.As<byte, C1>(ref Unsafe.AddByteOffset(ref byteRef, c1Offset)),
                        ref Unsafe.As<byte, C2>(ref Unsafe.AddByteOffset(ref byteRef, c2Offset)),
                        ref Unsafe.As<byte, C3>(ref Unsafe.AddByteOffset(ref byteRef, c3Offset)))) {
                        handler(new(slot, host));
                    }
                }
            }, runner);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Filter<TRunner, C1, C2, C3, C4>(
        this IEntityHost host, ComponentFilter<C1, C2, C3, C4> filter, EntityHandler handler, TRunner runner)
        where TRunner : IRunner
        => host.Handle((filter, handler),
            static (IEntityHost host, in (ComponentFilter<C1, C2, C3, C4>, EntityHandler) fs, int from, int to) => {
                var desc = host.Descriptor;
                var slots = host.AllocatedSlots;
                var (filter, handler) = fs;

                var c1Offset = desc.GetOffset<C1>();
                var c2Offset = desc.GetOffset<C2>();
                var c3Offset = desc.GetOffset<C3>();
                var c4Offset = desc.GetOffset<C4>();

                for (int i = from; i != to; ++i) {
                    ref readonly var slot = ref slots[i];
                    ref var byteRef = ref host.UnsafeGetByteRef(slot);

                    if (filter(
                        ref Unsafe.As<byte, C1>(ref Unsafe.AddByteOffset(ref byteRef, c1Offset)),
                        ref Unsafe.As<byte, C2>(ref Unsafe.AddByteOffset(ref byteRef, c2Offset)),
                        ref Unsafe.As<byte, C3>(ref Unsafe.AddByteOffset(ref byteRef, c3Offset)),
                        ref Unsafe.As<byte, C4>(ref Unsafe.AddByteOffset(ref byteRef, c4Offset)))) {
                        handler(new(slot, host));
                    }
                }
            }, runner);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Filter<TRunner, C1, C2, C3, C4, C5>(
        this IEntityHost host, ComponentFilter<C1, C2, C3, C4, C5> filter, EntityHandler handler, TRunner runner)
        where TRunner : IRunner
        => host.Handle((filter, handler),
            static (IEntityHost host, in (ComponentFilter<C1, C2, C3, C4, C5>, EntityHandler) fs, int from, int to) => {
                var desc = host.Descriptor;
                var slots = host.AllocatedSlots;
                var (filter, handler) = fs;

                var c1Offset = desc.GetOffset<C1>();
                var c2Offset = desc.GetOffset<C2>();
                var c3Offset = desc.GetOffset<C3>();
                var c4Offset = desc.GetOffset<C4>();
                var c5Offset = desc.GetOffset<C5>();

                for (int i = from; i != to; ++i) {
                    ref readonly var slot = ref slots[i];
                    ref var byteRef = ref host.UnsafeGetByteRef(slot);

                    if (filter(
                        ref Unsafe.As<byte, C1>(ref Unsafe.AddByteOffset(ref byteRef, c1Offset)),
                        ref Unsafe.As<byte, C2>(ref Unsafe.AddByteOffset(ref byteRef, c2Offset)),
                        ref Unsafe.As<byte, C3>(ref Unsafe.AddByteOffset(ref byteRef, c3Offset)),
                        ref Unsafe.As<byte, C4>(ref Unsafe.AddByteOffset(ref byteRef, c4Offset)),
                        ref Unsafe.As<byte, C5>(ref Unsafe.AddByteOffset(ref byteRef, c5Offset)))) {
                        handler(new(slot, host));
                    }
                }
            }, runner);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Filter<TRunner, C1, C2, C3, C4, C5, C6>(
        this IEntityHost host, ComponentFilter<C1, C2, C3, C4, C5, C6> filter, EntityHandler handler, TRunner runner)
        where TRunner : IRunner
        => host.Handle((filter, handler),
            static (IEntityHost host, in (ComponentFilter<C1, C2, C3, C4, C5, C6>, EntityHandler) fs, int from, int to) => {
                var desc = host.Descriptor;
                var slots = host.AllocatedSlots;
                var (filter, handler) = fs;

                var c1Offset = desc.GetOffset<C1>();
                var c2Offset = desc.GetOffset<C2>();
                var c3Offset = desc.GetOffset<C3>();
                var c4Offset = desc.GetOffset<C4>();
                var c5Offset = desc.GetOffset<C5>();
                var c6Offset = desc.GetOffset<C6>();

                for (int i = from; i != to; ++i) {
                    ref readonly var slot = ref slots[i];
                    ref var byteRef = ref host.UnsafeGetByteRef(slot);

                    if (filter(
                        ref Unsafe.As<byte, C1>(ref Unsafe.AddByteOffset(ref byteRef, c1Offset)),
                        ref Unsafe.As<byte, C2>(ref Unsafe.AddByteOffset(ref byteRef, c2Offset)),
                        ref Unsafe.As<byte, C3>(ref Unsafe.AddByteOffset(ref byteRef, c3Offset)),
                        ref Unsafe.As<byte, C4>(ref Unsafe.AddByteOffset(ref byteRef, c4Offset)),
                        ref Unsafe.As<byte, C5>(ref Unsafe.AddByteOffset(ref byteRef, c5Offset)),
                        ref Unsafe.As<byte, C6>(ref Unsafe.AddByteOffset(ref byteRef, c6Offset)))) {
                        handler(new(slot, host));
                    }
                }
            }, runner);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Filter<TRunner, TData, C1>(
        this IEntityHost host, in TData userData, DataComponentFilter<TData, C1> filter, EntityHandler<TData> handler, TRunner runner)
        where TRunner : IRunner
        => host.Handle((filter, handler, userData),
            static (IEntityHost host, in (DataComponentFilter<TData, C1>, EntityHandler<TData>, TData) data, int from, int to) => {
                var desc = host.Descriptor;
                var slots = host.AllocatedSlots;

                var c1Offset = desc.GetOffset<C1>();

                var filter = data.Item1;
                var handler = data.Item2;
                ref readonly var userData = ref data.Item3;

                for (int i = from; i != to; ++i) {
                    ref readonly var slot = ref slots[i];
                    ref var byteRef = ref host.UnsafeGetByteRef(slot);
                    
                    if (filter(userData, ref Unsafe.As<byte, C1>(ref Unsafe.AddByteOffset(ref byteRef, c1Offset)))) {
                        handler(userData, new(slot, host));
                    }
                }
            }, runner);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Filter<TRunner, TData, C1, C2>(
        this IEntityHost host, in TData userData, DataComponentFilter<TData, C1, C2> filter, EntityHandler<TData> handler, TRunner runner)
        where TRunner : IRunner
        => host.Handle((filter, handler, userData),
            static (IEntityHost host, in (DataComponentFilter<TData, C1, C2>, EntityHandler<TData>, TData) data, int from, int to) => {
                var desc = host.Descriptor;
                var slots = host.AllocatedSlots;

                var c1Offset = desc.GetOffset<C1>();
                var c2Offset = desc.GetOffset<C2>();

                var filter = data.Item1;
                var handler = data.Item2;
                ref readonly var userData = ref data.Item3;

                for (int i = from; i != to; ++i) {
                    ref readonly var slot = ref slots[i];
                    ref var byteRef = ref host.UnsafeGetByteRef(slot);

                    if (filter(userData,
                        ref Unsafe.As<byte, C1>(ref Unsafe.AddByteOffset(ref byteRef, c1Offset)),
                        ref Unsafe.As<byte, C2>(ref Unsafe.AddByteOffset(ref byteRef, c2Offset)))) {
                        handler(userData, new(slot, host));
                    }
                }
            }, runner);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Filter<TRunner, TData, C1, C2, C3>(
        this IEntityHost host, in TData userData, DataComponentFilter<TData, C1, C2, C3> filter, EntityHandler<TData> handler, TRunner runner)
        where TRunner : IRunner
        => host.Handle((filter, handler, userData),
            static (IEntityHost host, in (DataComponentFilter<TData, C1, C2, C3>, EntityHandler<TData>, TData) data, int from, int to) => {
                var desc = host.Descriptor;
                var slots = host.AllocatedSlots;

                var c1Offset = desc.GetOffset<C1>();
                var c2Offset = desc.GetOffset<C2>();
                var c3Offset = desc.GetOffset<C3>();

                var filter = data.Item1;
                var handler = data.Item2;
                ref readonly var userData = ref data.Item3;

                for (int i = from; i != to; ++i) {
                    ref readonly var slot = ref slots[i];
                    ref var byteRef = ref host.UnsafeGetByteRef(slot);

                    if (filter(userData,
                        ref Unsafe.As<byte, C1>(ref Unsafe.AddByteOffset(ref byteRef, c1Offset)),
                        ref Unsafe.As<byte, C2>(ref Unsafe.AddByteOffset(ref byteRef, c2Offset)),
                        ref Unsafe.As<byte, C3>(ref Unsafe.AddByteOffset(ref byteRef, c3Offset)))) {
                        handler(userData, new(slot, host));
                    }
                }
            }, runner);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Filter<TRunner, TData, C1, C2, C3, C4>(
        this IEntityHost host, in TData userData, DataComponentFilter<TData, C1, C2, C3, C4> filter, EntityHandler<TData> handler, TRunner runner)
        where TRunner : IRunner
        => host.Handle((filter, handler, userData),
            static (IEntityHost host, in (DataComponentFilter<TData, C1, C2, C3, C4>, EntityHandler<TData>, TData) data, int from, int to) => {
                var desc = host.Descriptor;
                var slots = host.AllocatedSlots;

                var c1Offset = desc.GetOffset<C1>();
                var c2Offset = desc.GetOffset<C2>();
                var c3Offset = desc.GetOffset<C3>();
                var c4Offset = desc.GetOffset<C4>();

                var filter = data.Item1;
                var handler = data.Item2;
                ref readonly var userData = ref data.Item3;

                for (int i = from; i != to; ++i) {
                    ref readonly var slot = ref slots[i];
                    ref var byteRef = ref host.UnsafeGetByteRef(slot);

                    if (filter(userData,
                        ref Unsafe.As<byte, C1>(ref Unsafe.AddByteOffset(ref byteRef, c1Offset)),
                        ref Unsafe.As<byte, C2>(ref Unsafe.AddByteOffset(ref byteRef, c2Offset)),
                        ref Unsafe.As<byte, C3>(ref Unsafe.AddByteOffset(ref byteRef, c3Offset)),
                        ref Unsafe.As<byte, C4>(ref Unsafe.AddByteOffset(ref byteRef, c4Offset)))) {
                        handler(userData, new(slot, host));
                    }
                }
            }, runner);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Filter<TRunner, TData, C1, C2, C3, C4, C5>(
        this IEntityHost host, in TData userData, DataComponentFilter<TData, C1, C2, C3, C4, C5> filter, EntityHandler<TData> handler, TRunner runner)
        where TRunner : IRunner
        => host.Handle((filter, handler, userData),
            static (IEntityHost host, in (DataComponentFilter<TData, C1, C2, C3, C4, C5>, EntityHandler<TData>, TData) data, int from, int to) => {
                var desc = host.Descriptor;
                var slots = host.AllocatedSlots;

                var c1Offset = desc.GetOffset<C1>();
                var c2Offset = desc.GetOffset<C2>();
                var c3Offset = desc.GetOffset<C3>();
                var c4Offset = desc.GetOffset<C4>();
                var c5Offset = desc.GetOffset<C5>();

                var filter = data.Item1;
                var handler = data.Item2;
                ref readonly var userData = ref data.Item3;

                for (int i = from; i != to; ++i) {
                    ref readonly var slot = ref slots[i];
                    ref var byteRef = ref host.UnsafeGetByteRef(slot);

                    if (filter(userData,
                        ref Unsafe.As<byte, C1>(ref Unsafe.AddByteOffset(ref byteRef, c1Offset)),
                        ref Unsafe.As<byte, C2>(ref Unsafe.AddByteOffset(ref byteRef, c2Offset)),
                        ref Unsafe.As<byte, C3>(ref Unsafe.AddByteOffset(ref byteRef, c3Offset)),
                        ref Unsafe.As<byte, C4>(ref Unsafe.AddByteOffset(ref byteRef, c4Offset)),
                        ref Unsafe.As<byte, C5>(ref Unsafe.AddByteOffset(ref byteRef, c5Offset)))) {
                        handler(userData, new(slot, host));
                    }
                }
            }, runner);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Filter<TRunner, TData, C1, C2, C3, C4, C5, C6>(
        this IEntityHost host, in TData userData, DataComponentFilter<TData, C1, C2, C3, C4, C5, C6> filter, EntityHandler<TData> handler, TRunner runner)
        where TRunner : IRunner
        => host.Handle((filter, handler, userData),
            static (IEntityHost host, in (DataComponentFilter<TData, C1, C2, C3, C4, C5, C6>, EntityHandler<TData>, TData) data, int from, int to) => {
                var desc = host.Descriptor;
                var slots = host.AllocatedSlots;

                var c1Offset = desc.GetOffset<C1>();
                var c2Offset = desc.GetOffset<C2>();
                var c3Offset = desc.GetOffset<C3>();
                var c4Offset = desc.GetOffset<C4>();
                var c5Offset = desc.GetOffset<C5>();
                var c6Offset = desc.GetOffset<C6>();

                var filter = data.Item1;
                var handler = data.Item2;
                ref readonly var userData = ref data.Item3;

                for (int i = from; i != to; ++i) {
                    ref readonly var slot = ref slots[i];
                    ref var byteRef = ref host.UnsafeGetByteRef(slot);

                    if (filter(userData,
                        ref Unsafe.As<byte, C1>(ref Unsafe.AddByteOffset(ref byteRef, c1Offset)),
                        ref Unsafe.As<byte, C2>(ref Unsafe.AddByteOffset(ref byteRef, c2Offset)),
                        ref Unsafe.As<byte, C3>(ref Unsafe.AddByteOffset(ref byteRef, c3Offset)),
                        ref Unsafe.As<byte, C4>(ref Unsafe.AddByteOffset(ref byteRef, c4Offset)),
                        ref Unsafe.As<byte, C5>(ref Unsafe.AddByteOffset(ref byteRef, c5Offset)),
                        ref Unsafe.As<byte, C6>(ref Unsafe.AddByteOffset(ref byteRef, c6Offset)))) {
                        handler(userData, new(slot, host));
                    }
                }
            }, runner);

    #region CurrentThreadRunner

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Filter<C1>(
        this IEntityHost host, ComponentFilter<C1> filter, EntityHandler handler)
        => host.Filter(filter, handler, CurrentThreadRunner.Instance);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Filter<C1, C2>(
        this IEntityHost host, ComponentFilter<C1, C2> filter, EntityHandler handler)
        => host.Filter(filter, handler, CurrentThreadRunner.Instance);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Filter<C1, C2, C3>(
        this IEntityHost host, ComponentFilter<C1, C2, C3> filter, EntityHandler handler)
        => host.Filter(filter, handler, CurrentThreadRunner.Instance);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Filter<C1, C2, C3, C4>(
        this IEntityHost host, ComponentFilter<C1, C2, C3, C4> filter, EntityHandler handler)
        => host.Filter(filter, handler, CurrentThreadRunner.Instance);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Filter<C1, C2, C3, C4, C5>(
        this IEntityHost host, ComponentFilter<C1, C2, C3, C4, C5> filter, EntityHandler handler)
        => host.Filter(filter, handler, CurrentThreadRunner.Instance);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Filter<C1, C2, C3, C4, C5, C6>(
        this IEntityHost host, ComponentFilter<C1, C2, C3, C4, C5, C6> filter, EntityHandler handler)
        => host.Filter(filter, handler, CurrentThreadRunner.Instance);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Filter<TData, C1>(
        this IEntityHost host, in TData userData, DataComponentFilter<TData, C1> filter, EntityHandler<TData> handler)
        => host.Filter(userData, filter, handler, CurrentThreadRunner.Instance);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Filter<TData, C1, C2>(
        this IEntityHost host, in TData userData, DataComponentFilter<TData, C1, C2> filter, EntityHandler<TData> handler)
        => host.Filter(userData, filter, handler, CurrentThreadRunner.Instance);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Filter<TData, C1, C2, C3>(
        this IEntityHost host, in TData userData, DataComponentFilter<TData, C1, C2, C3> filter, EntityHandler<TData> handler)
        => host.Filter(userData, filter, handler, CurrentThreadRunner.Instance);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Filter<TData, C1, C2, C3, C4>(
        this IEntityHost host, in TData userData, DataComponentFilter<TData, C1, C2, C3, C4> filter, EntityHandler<TData> handler)
        => host.Filter(userData, filter, handler, CurrentThreadRunner.Instance);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Filter<TData, C1, C2, C3, C4, C5>(
        this IEntityHost host, in TData userData, DataComponentFilter<TData, C1, C2, C3, C4, C5> filter, EntityHandler<TData> handler)
        => host.Filter(userData, filter, handler, CurrentThreadRunner.Instance);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Filter<TData, C1, C2, C3, C4, C5, C6>(
        this IEntityHost host, in TData userData, DataComponentFilter<TData, C1, C2, C3, C4, C5, C6> filter, EntityHandler<TData> handler)
        => host.Filter(userData, filter, handler, CurrentThreadRunner.Instance);
    
    #endregion // ParallelRunner

    #region ParallelRunner

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void FilterOnParallel<C1>(
        this IEntityHost host, ComponentFilter<C1> filter, EntityHandler handler)
        => host.Filter(filter, handler, ParallelRunner.Default);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void FilterOnParallel<C1, C2>(
        this IEntityHost host, ComponentFilter<C1, C2> filter, EntityHandler handler)
        => host.Filter(filter, handler, ParallelRunner.Default);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void FilterOnParallel<C1, C2, C3>(
        this IEntityHost host, ComponentFilter<C1, C2, C3> filter, EntityHandler handler)
        => host.Filter(filter, handler, ParallelRunner.Default);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void FilterOnParallel<C1, C2, C3, C4>(
        this IEntityHost host, ComponentFilter<C1, C2, C3, C4> filter, EntityHandler handler)
        => host.Filter(filter, handler, ParallelRunner.Default);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void FilterOnParallel<C1, C2, C3, C4, C5>(
        this IEntityHost host, ComponentFilter<C1, C2, C3, C4, C5> filter, EntityHandler handler)
        => host.Filter(filter, handler, ParallelRunner.Default);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void FilterOnParallel<C1, C2, C3, C4, C5, C6>(
        this IEntityHost host, ComponentFilter<C1, C2, C3, C4, C5, C6> filter, EntityHandler handler)
        => host.Filter(filter, handler, ParallelRunner.Default);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void FilterOnParallel<TData, C1>(
        this IEntityHost host, in TData userData, DataComponentFilter<TData, C1> filter, EntityHandler<TData> handler)
        => host.Filter(userData, filter, handler, ParallelRunner.Default);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void FilterOnParallel<TData, C1, C2>(
        this IEntityHost host, in TData userData, DataComponentFilter<TData, C1, C2> filter, EntityHandler<TData> handler)
        => host.Filter(userData, filter, handler, ParallelRunner.Default);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void FilterOnParallel<TData, C1, C2, C3>(
        this IEntityHost host, in TData userData, DataComponentFilter<TData, C1, C2, C3> filter, EntityHandler<TData> handler)
        => host.Filter(userData, filter, handler, ParallelRunner.Default);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void FilterOnParallel<TData, C1, C2, C3, C4>(
        this IEntityHost host, in TData userData, DataComponentFilter<TData, C1, C2, C3, C4> filter, EntityHandler<TData> handler)
        => host.Filter(userData, filter, handler, ParallelRunner.Default);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void FilterOnParallel<TData, C1, C2, C3, C4, C5>(
        this IEntityHost host, in TData userData, DataComponentFilter<TData, C1, C2, C3, C4, C5> filter, EntityHandler<TData> handler)
        => host.Filter(userData, filter, handler, ParallelRunner.Default);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void FilterOnParallel<TData, C1, C2, C3, C4, C5, C6>(
        this IEntityHost host, in TData userData, DataComponentFilter<TData, C1, C2, C3, C4, C5, C6> filter, EntityHandler<TData> handler)
        => host.Filter(userData, filter, handler, ParallelRunner.Default);
    
    #endregion // ParallelRunner

    #endregion // ComponentFilter
}