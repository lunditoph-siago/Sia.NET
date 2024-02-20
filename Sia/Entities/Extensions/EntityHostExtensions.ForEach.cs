namespace Sia;

using System.Runtime.CompilerServices;

public static partial class EntityHostExtensions
{
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
    public static unsafe void ForEach<TRunner, TUserData>(
        this IEntityHost host, in TUserData userData, EntityHandler<TUserData> handler, TRunner runner)
        where TRunner : IRunner
        => host.Handle((userData, handler),
            static (IEntityHost host, in (TUserData, EntityHandler<TUserData>) entry, int from, int to) => {
                ref readonly var data = ref entry.Item1;
                var handler = entry.Item2;
                var slosts = host.AllocatedSlots;
                for (int i = from; i != to; ++i) {
                    handler(data, new(slosts[i], host));
                }
            }, runner);

    #region CurrentThreadRunner

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void ForEach(this IEntityHost host, EntityHandler handler)
        => host.ForEach(handler, CurrentThreadRunner.Instance);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void ForEach<TUserData>(
        this IEntityHost host, in TUserData data, EntityHandler<TUserData> handler)
        => host.ForEach(data, handler, CurrentThreadRunner.Instance);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void ForEach(this IEntityHost host, SimpleEntityHandler handler)
        => host.ForEach(handler,
            static (in SimpleEntityHandler handler, in EntityRef entity) => handler(entity));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void ForEach<TData>(this IEntityHost host, in TData data, SimpleEntityHandler<TData> handler)
        => host.ForEach((handler, data),
            static (in (SimpleEntityHandler<TData>, TData) data, in EntityRef entity) => data.Item1(data.Item2, entity));
    
    #endregion

    #region ParallelRunner

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void ForEachOnParallel(this IEntityHost host, EntityHandler handler)
        => host.ForEach(handler, ParallelRunner.Default);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void ForEachOnParallel<TUserData>(
        this IEntityHost host, in TUserData data, EntityHandler<TUserData> handler)
        => host.ForEach(data, handler, ParallelRunner.Default);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void ForEachOnParallel(this IEntityHost host, SimpleEntityHandler handler)
        => host.ForEachOnParallel(handler,
            static (in SimpleEntityHandler handler, in EntityRef entity) => handler(entity));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void ForEachOnParallel<TData>(this IEntityHost host, in TData data, SimpleEntityHandler<TData> handler)
        => host.ForEachOnParallel((handler, data),
            static (in (SimpleEntityHandler<TData>, TData) data, in EntityRef entity) => data.Item1(data.Item2, entity));
    
    #endregion

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForEach<C1>(
        this IEntityHost host, ComponentHandler<C1> updator)
    {
        var desc = host.Descriptor;

        var c1Offset = desc.GetOffset<C1>();

        foreach (ref readonly var slot in host.AllocatedSlots) {
            var ptr = (IntPtr)Unsafe.AsPointer(ref host.UnsafeGetByteRef(slot));
            updator(ref Unsafe.AsRef<C1>((void*)(ptr + c1Offset)));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForEach<C1, C2>(
        this IEntityHost host, ComponentHandler<C1, C2> updator)
    {
        var desc = host.Descriptor;

        var c1Offset = desc.GetOffset<C1>();
        var c2Offset = desc.GetOffset<C2>();

        foreach (ref readonly var slot in host.AllocatedSlots) {
            var ptr = (IntPtr)Unsafe.AsPointer(ref host.UnsafeGetByteRef(slot));
            updator(
                ref Unsafe.AsRef<C1>((void*)(ptr + c1Offset)),
                ref Unsafe.AsRef<C2>((void*)(ptr + c2Offset)));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForEach<C1, C2, C3>(
        this IEntityHost host, ComponentHandler<C1, C2, C3> updator)
    {
        var desc = host.Descriptor;

        var c1Offset = desc.GetOffset<C1>();
        var c2Offset = desc.GetOffset<C2>();
        var c3Offset = desc.GetOffset<C3>();

        foreach (ref readonly var slot in host.AllocatedSlots) {
            var ptr = (IntPtr)Unsafe.AsPointer(ref host.UnsafeGetByteRef(slot));
            updator(
                ref Unsafe.AsRef<C1>((void*)(ptr + c1Offset)),
                ref Unsafe.AsRef<C2>((void*)(ptr + c2Offset)),
                ref Unsafe.AsRef<C3>((void*)(ptr + c3Offset)));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForEach<C1, C2, C3, C4>(
        this IEntityHost host, ComponentHandler<C1, C2, C3, C4> updator)
    {
        var desc = host.Descriptor;

        var c1Offset = desc.GetOffset<C1>();
        var c2Offset = desc.GetOffset<C2>();
        var c3Offset = desc.GetOffset<C3>();
        var c4Offset = desc.GetOffset<C4>();

        foreach (ref readonly var slot in host.AllocatedSlots) {
            var ptr = (IntPtr)Unsafe.AsPointer(ref host.UnsafeGetByteRef(slot));
            updator(
                ref Unsafe.AsRef<C1>((void*)(ptr + c1Offset)),
                ref Unsafe.AsRef<C2>((void*)(ptr + c2Offset)),
                ref Unsafe.AsRef<C3>((void*)(ptr + c3Offset)),
                ref Unsafe.AsRef<C4>((void*)(ptr + c4Offset)));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForEach<C1, C2, C3, C4, C5>(
        this IEntityHost host, ComponentHandler<C1, C2, C3, C4, C5> updator)
    {
        var desc = host.Descriptor;

        var c1Offset = desc.GetOffset<C1>();
        var c2Offset = desc.GetOffset<C2>();
        var c3Offset = desc.GetOffset<C3>();
        var c4Offset = desc.GetOffset<C4>();
        var c5Offset = desc.GetOffset<C5>();

        foreach (ref readonly var slot in host.AllocatedSlots) {
            var ptr = (IntPtr)Unsafe.AsPointer(ref host.UnsafeGetByteRef(slot));
            updator(
                ref Unsafe.AsRef<C1>((void*)(ptr + c1Offset)),
                ref Unsafe.AsRef<C2>((void*)(ptr + c2Offset)),
                ref Unsafe.AsRef<C3>((void*)(ptr + c3Offset)),
                ref Unsafe.AsRef<C4>((void*)(ptr + c4Offset)),
                ref Unsafe.AsRef<C5>((void*)(ptr + c5Offset)));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForEach<C1, C2, C3, C4, C5, C6>(
        this IEntityHost host, ComponentHandler<C1, C2, C3, C4, C5, C6> updator)
    {
        var desc = host.Descriptor;

        var c1Offset = desc.GetOffset<C1>();
        var c2Offset = desc.GetOffset<C2>();
        var c3Offset = desc.GetOffset<C3>();
        var c4Offset = desc.GetOffset<C4>();
        var c5Offset = desc.GetOffset<C5>();
        var c6Offset = desc.GetOffset<C6>();

        foreach (ref readonly var slot in host.AllocatedSlots) {
            var ptr = (IntPtr)Unsafe.AsPointer(ref host.UnsafeGetByteRef(slot));
            updator(
                ref Unsafe.AsRef<C1>((void*)(ptr + c1Offset)),
                ref Unsafe.AsRef<C2>((void*)(ptr + c2Offset)),
                ref Unsafe.AsRef<C3>((void*)(ptr + c3Offset)),
                ref Unsafe.AsRef<C4>((void*)(ptr + c4Offset)),
                ref Unsafe.AsRef<C5>((void*)(ptr + c5Offset)),
                ref Unsafe.AsRef<C6>((void*)(ptr + c6Offset)));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForEach<TRunner, C1>(
        this IEntityHost host, ComponentHandler<C1> updator, TRunner runner)
        where TRunner : IRunner
        => host.Handle(updator,
            static (IEntityHost host, in ComponentHandler<C1> updator, int from, int to) => {
                var desc = host.Descriptor;
                var slots = host.AllocatedSlots;

                var c1Offset = desc.GetOffset<C1>();

                for (int i = from; i != to; ++i) {
                    var ptr = (IntPtr)Unsafe.AsPointer(ref host.UnsafeGetByteRef(slots[i]));
                    updator(ref Unsafe.AsRef<C1>((void*)(ptr + c1Offset)));
                }
            }, runner);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForEach<TRunner, C1, C2>(
        this IEntityHost host, ComponentHandler<C1, C2> updator, TRunner runner)
        where TRunner : IRunner
        => host.Handle(updator,
            static (IEntityHost host, in ComponentHandler<C1, C2> updator, int from, int to) => {
                var desc = host.Descriptor;
                var slots = host.AllocatedSlots;

                var c1Offset = desc.GetOffset<C1>();
                var c2Offset = desc.GetOffset<C2>();

                for (int i = from; i != to; ++i) {
                    var ptr = (IntPtr)Unsafe.AsPointer(ref host.UnsafeGetByteRef(slots[i]));
                    updator(
                        ref Unsafe.AsRef<C1>((void*)(ptr + c1Offset)),
                        ref Unsafe.AsRef<C2>((void*)(ptr + c2Offset)));
                }
            }, runner);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForEach<TRunner, C1, C2, C3>(
        this IEntityHost host, ComponentHandler<C1, C2, C3> updator, TRunner runner)
        where TRunner : IRunner
        => host.Handle(updator,
            static (IEntityHost host, in ComponentHandler<C1, C2, C3> updator, int from, int to) => {
                var desc = host.Descriptor;
                var slots = host.AllocatedSlots;

                var c1Offset = desc.GetOffset<C1>();
                var c2Offset = desc.GetOffset<C2>();
                var c3Offset = desc.GetOffset<C3>();

                for (int i = from; i != to; ++i) {
                    var ptr = (IntPtr)Unsafe.AsPointer(ref host.UnsafeGetByteRef(slots[i]));
                    updator(
                        ref Unsafe.AsRef<C1>((void*)(ptr + c1Offset)),
                        ref Unsafe.AsRef<C2>((void*)(ptr + c2Offset)),
                        ref Unsafe.AsRef<C3>((void*)(ptr + c3Offset)));
                }
            }, runner);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForEach<TRunner, C1, C2, C3, C4>(
        this IEntityHost host, ComponentHandler<C1, C2, C3, C4> updator, TRunner runner)
        where TRunner : IRunner
        => host.Handle(updator,
            static (IEntityHost host, in ComponentHandler<C1, C2, C3, C4> updator, int from, int to) => {
                var desc = host.Descriptor;
                var slots = host.AllocatedSlots;

                var c1Offset = desc.GetOffset<C1>();
                var c2Offset = desc.GetOffset<C2>();
                var c3Offset = desc.GetOffset<C3>();
                var c4Offset = desc.GetOffset<C4>();

                for (int i = from; i != to; ++i) {
                    var ptr = (IntPtr)Unsafe.AsPointer(ref host.UnsafeGetByteRef(slots[i]));
                    updator(
                        ref Unsafe.AsRef<C1>((void*)(ptr + c1Offset)),
                        ref Unsafe.AsRef<C2>((void*)(ptr + c2Offset)),
                        ref Unsafe.AsRef<C3>((void*)(ptr + c3Offset)),
                        ref Unsafe.AsRef<C4>((void*)(ptr + c4Offset)));
                }
            }, runner);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForEach<TRunner, C1, C2, C3, C4, C5>(
        this IEntityHost host, ComponentHandler<C1, C2, C3, C4, C5> updator, TRunner runner)
        where TRunner : IRunner
        => host.Handle(updator,
            static (IEntityHost host, in ComponentHandler<C1, C2, C3, C4, C5> updator, int from, int to) => {
                var desc = host.Descriptor;
                var slots = host.AllocatedSlots;

                var c1Offset = desc.GetOffset<C1>();
                var c2Offset = desc.GetOffset<C2>();
                var c3Offset = desc.GetOffset<C3>();
                var c4Offset = desc.GetOffset<C4>();
                var c5Offset = desc.GetOffset<C5>();

                for (int i = from; i != to; ++i) {
                    var ptr = (IntPtr)Unsafe.AsPointer(ref host.UnsafeGetByteRef(slots[i]));
                    updator(
                        ref Unsafe.AsRef<C1>((void*)(ptr + c1Offset)),
                        ref Unsafe.AsRef<C2>((void*)(ptr + c2Offset)),
                        ref Unsafe.AsRef<C3>((void*)(ptr + c3Offset)),
                        ref Unsafe.AsRef<C4>((void*)(ptr + c4Offset)),
                        ref Unsafe.AsRef<C5>((void*)(ptr + c5Offset)));
                }
            }, runner);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForEach<TRunner, C1, C2, C3, C4, C5, C6>(
        this IEntityHost host, ComponentHandler<C1, C2, C3, C4, C5, C6> updator, TRunner runner)
        where TRunner : IRunner
        => host.Handle(updator,
            static (IEntityHost host, in ComponentHandler<C1, C2, C3, C4, C5, C6> updator, int from, int to) => {
                var desc = host.Descriptor;
                var slots = host.AllocatedSlots;

                var c1Offset = desc.GetOffset<C1>();
                var c2Offset = desc.GetOffset<C2>();
                var c3Offset = desc.GetOffset<C3>();
                var c4Offset = desc.GetOffset<C4>();
                var c5Offset = desc.GetOffset<C5>();
                var c6Offset = desc.GetOffset<C6>();

                for (int i = from; i != to; ++i) {
                    var ptr = (IntPtr)Unsafe.AsPointer(ref host.UnsafeGetByteRef(slots[i]));
                    updator(
                        ref Unsafe.AsRef<C1>((void*)(ptr + c1Offset)),
                        ref Unsafe.AsRef<C2>((void*)(ptr + c2Offset)),
                        ref Unsafe.AsRef<C3>((void*)(ptr + c3Offset)),
                        ref Unsafe.AsRef<C4>((void*)(ptr + c4Offset)),
                        ref Unsafe.AsRef<C5>((void*)(ptr + c5Offset)),
                        ref Unsafe.AsRef<C6>((void*)(ptr + c6Offset)));
                }
            }, runner);

    #region ParallelRunner

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForEachOnParallel<C1>(
        this IEntityHost host, ComponentHandler<C1> updator)
        => host.ForEach(updator, ParallelRunner.Default);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForEachOnParallel<C1, C2>(
        this IEntityHost host, ComponentHandler<C1, C2> updator)
        => host.ForEach(updator, ParallelRunner.Default);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForEachOnParallel<C1, C2, C3>(
        this IEntityHost host, ComponentHandler<C1, C2, C3> updator)
        => host.ForEach(updator, ParallelRunner.Default);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForEachOnParallel<C1, C2, C3, C4>(
        this IEntityHost host, ComponentHandler<C1, C2, C3, C4> updator)
        => host.ForEach(updator, ParallelRunner.Default);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForEachOnParallel<C1, C2, C3, C4, C5>(
        this IEntityHost host, ComponentHandler<C1, C2, C3, C4, C5> updator)
        => host.ForEach(updator, ParallelRunner.Default);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void ForEachOnParallel<C1, C2, C3, C4, C5, C6>(
        this IEntityHost host, ComponentHandler<C1, C2, C3, C4, C5, C6> updator)
        => host.ForEach(updator, ParallelRunner.Default);
    
    #endregion
}