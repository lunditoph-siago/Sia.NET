using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static Sia.EntityExtensionsCommon;

namespace Sia;

public static partial class EntityQueryExtensions
{
    private const int MinimumManagedSliceLength = 64;

    private readonly struct DelegateSliceHandler<C1>(ComponentHandler<C1> handler)
        : ISliceHandler<C1>
    {
        public void Handle(ref C1 c1) => handler(ref c1);
    }

    private readonly struct DelegateSliceHandler<C1, C2>(ComponentHandler<C1, C2> handler)
        : ISliceHandler<C1, C2>
    {
        public void Handle(ref C1 c1, ref C2 c2) => handler(ref c1, ref c2);
    }

    private readonly struct DelegateSliceHandler<C1, C2, C3>(ComponentHandler<C1, C2, C3> handler)
        : ISliceHandler<C1, C2, C3>
    {
        public void Handle(ref C1 c1, ref C2 c2, ref C3 c3) => handler(ref c1, ref c2, ref c3);
    }

    private readonly struct DelegateSliceHandler<C1, C2, C3, C4>(ComponentHandler<C1, C2, C3, C4> handler)
        : ISliceHandler<C1, C2, C3, C4>
    {
        public void Handle(ref C1 c1, ref C2 c2, ref C3 c3, ref C4 c4) => handler(ref c1, ref c2, ref c3, ref c4);
    }

    private readonly struct DelegateSliceHandler<C1, C2, C3, C4, C5>(ComponentHandler<C1, C2, C3, C4, C5> handler)
        : ISliceHandler<C1, C2, C3, C4, C5>
    {
        public void Handle(ref C1 c1, ref C2 c2, ref C3 c3, ref C4 c4, ref C5 c5) => handler(ref c1, ref c2, ref c3, ref c4, ref c5);
    }

    private readonly struct DelegateSliceHandler<C1, C2, C3, C4, C5, C6>(ComponentHandler<C1, C2, C3, C4, C5, C6> handler)
        : ISliceHandler<C1, C2, C3, C4, C5, C6>
    {
        public void Handle(ref C1 c1, ref C2 c2, ref C3 c3, ref C4 c4, ref C5 c5, ref C6 c6) => handler(ref c1, ref c2, ref c3, ref c4, ref c5, ref c6);
    }

    public static void ForSlice<C1, THandler>(
        this IEntityQuery query, ref THandler handler)
        where THandler : ISliceHandler<C1>
    {
        var hosts = query.Hosts;
        var hostCount = hosts.Count;
        for (var h = 0; h < hostCount; h++) {
            var host = hosts[h];
            var count = host.Count;
            if (count == 0) {
                continue;
            }
            var desc = host.Descriptor;
            var c1Offset = desc.GetOffset<C1>();
            var version = host.Version;

            if (host.TryGetSequentialBytes(out var bytes)) {
                var size = desc.MemorySize;
                ref var cursor = ref MemoryMarshal.GetReference(bytes);
                for (var i = 0; i < count; i++) {
                    handler.Handle(ref c1Offset.Get(ref cursor));
                    cursor = ref Unsafe.AddByteOffset(ref cursor, size);
                }
                GuardSequentialBytes(host, bytes);
            }
            else if (count < MinimumManagedSliceLength || !TryManagedSlice(host, desc, 0, count, c1Offset, ref handler)) {
                for (var i = 0; i < count; i++) {
                    ref var byteRef = ref host.GetByteRef(i);
                    handler.Handle(ref c1Offset.Get(ref byteRef));
                }
            }
            GuardVersion(version, host.Version);
        }
    }

    public static void ForSlice<C1, C2, THandler>(
        this IEntityQuery query, ref THandler handler)
        where THandler : ISliceHandler<C1, C2>
    {
        var hosts = query.Hosts;
        var hostCount = hosts.Count;
        for (var h = 0; h < hostCount; h++) {
            var host = hosts[h];
            var count = host.Count;
            if (count == 0) {
                continue;
            }
            var desc = host.Descriptor;
            var c1Offset = desc.GetOffset<C1>();
            var c2Offset = desc.GetOffset<C2>();
            var version = host.Version;

            if (host.TryGetSequentialBytes(out var bytes)) {
                var size = desc.MemorySize;
                ref var cursor = ref MemoryMarshal.GetReference(bytes);
                for (var i = 0; i < count; i++) {
                    handler.Handle(
                        ref c1Offset.Get(ref cursor),
                        ref c2Offset.Get(ref cursor));
                    cursor = ref Unsafe.AddByteOffset(ref cursor, size);
                }
                GuardSequentialBytes(host, bytes);
            }
            else if (count < MinimumManagedSliceLength || !TryManagedSlice(host, desc, 0, count, c1Offset, c2Offset, ref handler)) {
                for (var i = 0; i < count; i++) {
                    ref var byteRef = ref host.GetByteRef(i);
                    handler.Handle(
                        ref c1Offset.Get(ref byteRef),
                        ref c2Offset.Get(ref byteRef));
                }
            }
            GuardVersion(version, host.Version);
        }
    }

    public static void ForSlice<C1, C2, C3, THandler>(
        this IEntityQuery query, ref THandler handler)
        where THandler : ISliceHandler<C1, C2, C3>
    {
        var hosts = query.Hosts;
        var hostCount = hosts.Count;
        for (var h = 0; h < hostCount; h++) {
            var host = hosts[h];
            var count = host.Count;
            if (count == 0) {
                continue;
            }
            var desc = host.Descriptor;
            var c1Offset = desc.GetOffset<C1>();
            var c2Offset = desc.GetOffset<C2>();
            var c3Offset = desc.GetOffset<C3>();
            var version = host.Version;

            if (host.TryGetSequentialBytes(out var bytes)) {
                var size = desc.MemorySize;
                ref var cursor = ref MemoryMarshal.GetReference(bytes);
                for (var i = 0; i < count; i++) {
                    handler.Handle(
                        ref c1Offset.Get(ref cursor),
                        ref c2Offset.Get(ref cursor),
                        ref c3Offset.Get(ref cursor));
                    cursor = ref Unsafe.AddByteOffset(ref cursor, size);
                }
                GuardSequentialBytes(host, bytes);
            }
            else if (count < MinimumManagedSliceLength || !TryManagedSlice(host, desc, 0, count, c1Offset, c2Offset, c3Offset, ref handler)) {
                for (var i = 0; i < count; i++) {
                    ref var byteRef = ref host.GetByteRef(i);
                    handler.Handle(
                        ref c1Offset.Get(ref byteRef),
                        ref c2Offset.Get(ref byteRef),
                        ref c3Offset.Get(ref byteRef));
                }
            }
            GuardVersion(version, host.Version);
        }
    }

    public static void ForSlice<C1, C2, C3, C4, THandler>(
        this IEntityQuery query, ref THandler handler)
        where THandler : ISliceHandler<C1, C2, C3, C4>
    {
        var hosts = query.Hosts;
        var hostCount = hosts.Count;
        for (var h = 0; h < hostCount; h++) {
            var host = hosts[h];
            var count = host.Count;
            if (count == 0) {
                continue;
            }
            var desc = host.Descriptor;
            var c1Offset = desc.GetOffset<C1>();
            var c2Offset = desc.GetOffset<C2>();
            var c3Offset = desc.GetOffset<C3>();
            var c4Offset = desc.GetOffset<C4>();
            var version = host.Version;

            if (host.TryGetSequentialBytes(out var bytes)) {
                var size = desc.MemorySize;
                ref var cursor = ref MemoryMarshal.GetReference(bytes);
                for (var i = 0; i < count; i++) {
                    handler.Handle(
                        ref c1Offset.Get(ref cursor),
                        ref c2Offset.Get(ref cursor),
                        ref c3Offset.Get(ref cursor),
                        ref c4Offset.Get(ref cursor));
                    cursor = ref Unsafe.AddByteOffset(ref cursor, size);
                }
                GuardSequentialBytes(host, bytes);
            }
            else if (count < MinimumManagedSliceLength || !TryManagedSlice(host, desc, 0, count, c1Offset, c2Offset, c3Offset, c4Offset, ref handler)) {
                for (var i = 0; i < count; i++) {
                    ref var byteRef = ref host.GetByteRef(i);
                    handler.Handle(
                        ref c1Offset.Get(ref byteRef),
                        ref c2Offset.Get(ref byteRef),
                        ref c3Offset.Get(ref byteRef),
                        ref c4Offset.Get(ref byteRef));
                }
            }
            GuardVersion(version, host.Version);
        }
    }

    public static void ForSlice<C1, C2, C3, C4, C5, THandler>(this IEntityQuery query, ref THandler handler)
        where THandler : ISliceHandler<C1, C2, C3, C4, C5>
    {
        var hosts = query.Hosts;
        var hostCount = hosts.Count;
        for (var h = 0; h < hostCount; h++) {
            var host = hosts[h];
            var count = host.Count;
            if (count == 0) continue;
            var desc = host.Descriptor;
            var c1Offset = desc.GetOffset<C1>();
            var c2Offset = desc.GetOffset<C2>();
            var c3Offset = desc.GetOffset<C3>();
            var c4Offset = desc.GetOffset<C4>();
            var c5Offset = desc.GetOffset<C5>();
            var version = host.Version;
            if (host.TryGetSequentialBytes(out var bytes)) {
                var size = desc.MemorySize;
                ref var cursor = ref MemoryMarshal.GetReference(bytes);
                for (var i = 0; i < count; i++) {
                    handler.Handle(
                        ref c1Offset.Get(ref cursor),
                        ref c2Offset.Get(ref cursor),
                        ref c3Offset.Get(ref cursor),
                        ref c4Offset.Get(ref cursor),
                        ref c5Offset.Get(ref cursor));
                    cursor = ref Unsafe.AddByteOffset(ref cursor, size);
                }
                GuardSequentialBytes(host, bytes);
            }
            else if (count < MinimumManagedSliceLength || !TryManagedSlice(host, desc, 0, count, c1Offset, c2Offset, c3Offset, c4Offset, c5Offset, ref handler)) {
                for (var i = 0; i < count; i++) {
                    ref var byteRef = ref host.GetByteRef(i);
                    handler.Handle(
                        ref c1Offset.Get(ref byteRef),
                        ref c2Offset.Get(ref byteRef),
                        ref c3Offset.Get(ref byteRef),
                        ref c4Offset.Get(ref byteRef),
                        ref c5Offset.Get(ref byteRef));
                }
            }
            GuardVersion(version, host.Version);
        }
    }

    public static void ForSlice<C1, C2, C3, C4, C5, C6, THandler>(this IEntityQuery query, ref THandler handler)
        where THandler : ISliceHandler<C1, C2, C3, C4, C5, C6>
    {
        var hosts = query.Hosts;
        var hostCount = hosts.Count;
        for (var h = 0; h < hostCount; h++) {
            var host = hosts[h];
            var count = host.Count;
            if (count == 0) continue;
            var desc = host.Descriptor;
            var c1Offset = desc.GetOffset<C1>();
            var c2Offset = desc.GetOffset<C2>();
            var c3Offset = desc.GetOffset<C3>();
            var c4Offset = desc.GetOffset<C4>();
            var c5Offset = desc.GetOffset<C5>();
            var c6Offset = desc.GetOffset<C6>();
            var version = host.Version;
            if (host.TryGetSequentialBytes(out var bytes)) {
                var size = desc.MemorySize;
                ref var cursor = ref MemoryMarshal.GetReference(bytes);
                for (var i = 0; i < count; i++) {
                    handler.Handle(
                        ref c1Offset.Get(ref cursor),
                        ref c2Offset.Get(ref cursor),
                        ref c3Offset.Get(ref cursor),
                        ref c4Offset.Get(ref cursor),
                        ref c5Offset.Get(ref cursor),
                        ref c6Offset.Get(ref cursor));
                    cursor = ref Unsafe.AddByteOffset(ref cursor, size);
                }
                GuardSequentialBytes(host, bytes);
            }
            else if (count < MinimumManagedSliceLength || !TryManagedSlice(host, desc, 0, count, c1Offset, c2Offset, c3Offset, c4Offset, c5Offset, c6Offset, ref handler)) {
                for (var i = 0; i < count; i++) {
                    ref var byteRef = ref host.GetByteRef(i);
                    handler.Handle(
                        ref c1Offset.Get(ref byteRef),
                        ref c2Offset.Get(ref byteRef),
                        ref c3Offset.Get(ref byteRef),
                        ref c4Offset.Get(ref byteRef),
                        ref c5Offset.Get(ref byteRef),
                        ref c6Offset.Get(ref byteRef));
                }
            }
            GuardVersion(version, host.Version);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ArrayBuffer<TEntity>? GetManagedSliceBuffer<TEntity>(IEntityHost host)
        where TEntity : struct, IHList
    {
        if (!RuntimeHelpers.IsReferenceOrContainsReferences<TEntity>()) return null;
        if (host is WorldEntityHost<TEntity, ArrayEntityHost<TEntity>> world
            && world.InnerHost.GetType() == typeof(ArrayEntityHost<TEntity>)) return world.InnerHost.Buffer;
        if (host.GetType() == typeof(ArrayEntityHost<TEntity>) && host is ArrayEntityHost<TEntity> array) return array.Buffer;
        if (host.GetType() == typeof(BufferEntityHost<TEntity, ArrayBuffer<TEntity>>)
            && host is BufferEntityHost<TEntity, ArrayBuffer<TEntity>> direct) return direct.Buffer;
        if (host.GetType() == typeof(BufferEntityHost<TEntity, IBuffer<TEntity>>)
            && host is BufferEntityHost<TEntity, IBuffer<TEntity>> wrapped
            && wrapped.Buffer is ArrayBuffer<TEntity> buffer) return buffer;
        return null;
    }

    private ref struct ManagedVisitor<C1, THandler>(IEntityHost host, int from, int to, ComponentOffset<C1> c1, ref THandler handler)
        : IGenericStructTypeHandler<IHList> where THandler : ISliceHandler<C1>
    {
        private ref THandler _handler = ref handler;
        public bool Used;
        public void Handle<TEntity>() where TEntity : struct, IHList
        {
            if (GetManagedSliceBuffer<TEntity>(host) is not { } buffer) return;
            Used = true;
            ref var target = ref _handler;
            var offset1 = c1;
            var end = to;
            for (var i = from; i < end; i++) {
                ref var row = ref Unsafe.As<TEntity, byte>(ref buffer.GetRef(i));
                target.Handle(ref offset1.Get(ref row));
            }
        }
    }

    private static bool TryManagedSlice<C1, THandler>(IEntityHost host, EntityDescriptor desc, int from, int to, ComponentOffset<C1> c1, ref THandler handler)
        where THandler : ISliceHandler<C1>
    {
        var visitor = new ManagedVisitor<C1, THandler>(host, from, to, c1, ref handler);
        desc.HandleType(ref visitor);
        return visitor.Used;
    }

    private ref struct ManagedVisitor<C1, C2, THandler>(IEntityHost host, int from, int to, ComponentOffset<C1> c1, ComponentOffset<C2> c2, ref THandler handler)
        : IGenericStructTypeHandler<IHList> where THandler : ISliceHandler<C1, C2>
    {
        private ref THandler _handler = ref handler;
        public bool Used;
        public void Handle<TEntity>() where TEntity : struct, IHList
        {
            if (GetManagedSliceBuffer<TEntity>(host) is not { } buffer) return;
            Used = true;
            ref var target = ref _handler;
            var offset1 = c1;
            var offset2 = c2;
            var end = to;
            for (var i = from; i < end; i++) {
                ref var row = ref Unsafe.As<TEntity, byte>(ref buffer.GetRef(i));
                target.Handle(ref offset1.Get(ref row), ref offset2.Get(ref row));
            }
        }
    }

    private static bool TryManagedSlice<C1, C2, THandler>(IEntityHost host, EntityDescriptor desc, int from, int to, ComponentOffset<C1> c1, ComponentOffset<C2> c2, ref THandler handler)
        where THandler : ISliceHandler<C1, C2>
    {
        var visitor = new ManagedVisitor<C1, C2, THandler>(host, from, to, c1, c2, ref handler);
        desc.HandleType(ref visitor);
        return visitor.Used;
    }

    private ref struct ManagedVisitor<C1, C2, C3, THandler>(IEntityHost host, int from, int to, ComponentOffset<C1> c1, ComponentOffset<C2> c2, ComponentOffset<C3> c3, ref THandler handler)
        : IGenericStructTypeHandler<IHList> where THandler : ISliceHandler<C1, C2, C3>
    {
        private ref THandler _handler = ref handler;
        public bool Used;
        public void Handle<TEntity>() where TEntity : struct, IHList
        {
            if (GetManagedSliceBuffer<TEntity>(host) is not { } buffer) return;
            Used = true;
            ref var target = ref _handler;
            var offset1 = c1;
            var offset2 = c2;
            var offset3 = c3;
            var end = to;
            for (var i = from; i < end; i++) {
                ref var row = ref Unsafe.As<TEntity, byte>(ref buffer.GetRef(i));
                target.Handle(ref offset1.Get(ref row), ref offset2.Get(ref row), ref offset3.Get(ref row));
            }
        }
    }

    private static bool TryManagedSlice<C1, C2, C3, THandler>(IEntityHost host, EntityDescriptor desc, int from, int to, ComponentOffset<C1> c1, ComponentOffset<C2> c2, ComponentOffset<C3> c3, ref THandler handler)
        where THandler : ISliceHandler<C1, C2, C3>
    {
        var visitor = new ManagedVisitor<C1, C2, C3, THandler>(host, from, to, c1, c2, c3, ref handler);
        desc.HandleType(ref visitor);
        return visitor.Used;
    }

    private ref struct ManagedVisitor<C1, C2, C3, C4, THandler>(IEntityHost host, int from, int to, ComponentOffset<C1> c1, ComponentOffset<C2> c2, ComponentOffset<C3> c3, ComponentOffset<C4> c4, ref THandler handler)
        : IGenericStructTypeHandler<IHList> where THandler : ISliceHandler<C1, C2, C3, C4>
    {
        private ref THandler _handler = ref handler;
        public bool Used;
        public void Handle<TEntity>() where TEntity : struct, IHList
        {
            if (GetManagedSliceBuffer<TEntity>(host) is not { } buffer) return;
            Used = true;
            ref var target = ref _handler;
            var offset1 = c1;
            var offset2 = c2;
            var offset3 = c3;
            var offset4 = c4;
            var end = to;
            for (var i = from; i < end; i++) {
                ref var row = ref Unsafe.As<TEntity, byte>(ref buffer.GetRef(i));
                target.Handle(ref offset1.Get(ref row), ref offset2.Get(ref row), ref offset3.Get(ref row), ref offset4.Get(ref row));
            }
        }
    }

    private static bool TryManagedSlice<C1, C2, C3, C4, THandler>(IEntityHost host, EntityDescriptor desc, int from, int to, ComponentOffset<C1> c1, ComponentOffset<C2> c2, ComponentOffset<C3> c3, ComponentOffset<C4> c4, ref THandler handler)
        where THandler : ISliceHandler<C1, C2, C3, C4>
    {
        var visitor = new ManagedVisitor<C1, C2, C3, C4, THandler>(host, from, to, c1, c2, c3, c4, ref handler);
        desc.HandleType(ref visitor);
        return visitor.Used;
    }

    private ref struct ManagedVisitor<C1, C2, C3, C4, C5, THandler>(IEntityHost host, int from, int to, ComponentOffset<C1> c1, ComponentOffset<C2> c2, ComponentOffset<C3> c3, ComponentOffset<C4> c4, ComponentOffset<C5> c5, ref THandler handler)
        : IGenericStructTypeHandler<IHList> where THandler : ISliceHandler<C1, C2, C3, C4, C5>
    {
        private ref THandler _handler = ref handler;
        public bool Used;
        public void Handle<TEntity>() where TEntity : struct, IHList
        {
            if (GetManagedSliceBuffer<TEntity>(host) is not { } buffer) return;
            Used = true;
            ref var target = ref _handler;
            var offset1 = c1;
            var offset2 = c2;
            var offset3 = c3;
            var offset4 = c4;
            var offset5 = c5;
            var end = to;
            for (var i = from; i < end; i++) {
                ref var row = ref Unsafe.As<TEntity, byte>(ref buffer.GetRef(i));
                target.Handle(ref offset1.Get(ref row), ref offset2.Get(ref row), ref offset3.Get(ref row), ref offset4.Get(ref row), ref offset5.Get(ref row));
            }
        }
    }

    private static bool TryManagedSlice<C1, C2, C3, C4, C5, THandler>(IEntityHost host, EntityDescriptor desc, int from, int to, ComponentOffset<C1> c1, ComponentOffset<C2> c2, ComponentOffset<C3> c3, ComponentOffset<C4> c4, ComponentOffset<C5> c5, ref THandler handler)
        where THandler : ISliceHandler<C1, C2, C3, C4, C5>
    {
        var visitor = new ManagedVisitor<C1, C2, C3, C4, C5, THandler>(host, from, to, c1, c2, c3, c4, c5, ref handler);
        desc.HandleType(ref visitor);
        return visitor.Used;
    }

    private ref struct ManagedVisitor<C1, C2, C3, C4, C5, C6, THandler>(IEntityHost host, int from, int to, ComponentOffset<C1> c1, ComponentOffset<C2> c2, ComponentOffset<C3> c3, ComponentOffset<C4> c4, ComponentOffset<C5> c5, ComponentOffset<C6> c6, ref THandler handler)
        : IGenericStructTypeHandler<IHList> where THandler : ISliceHandler<C1, C2, C3, C4, C5, C6>
    {
        private ref THandler _handler = ref handler;
        public bool Used;
        public void Handle<TEntity>() where TEntity : struct, IHList
        {
            if (GetManagedSliceBuffer<TEntity>(host) is not { } buffer) return;
            Used = true;
            ref var target = ref _handler;
            var offset1 = c1;
            var offset2 = c2;
            var offset3 = c3;
            var offset4 = c4;
            var offset5 = c5;
            var offset6 = c6;
            var end = to;
            for (var i = from; i < end; i++) {
                ref var row = ref Unsafe.As<TEntity, byte>(ref buffer.GetRef(i));
                target.Handle(ref offset1.Get(ref row), ref offset2.Get(ref row), ref offset3.Get(ref row), ref offset4.Get(ref row), ref offset5.Get(ref row), ref offset6.Get(ref row));
            }
        }
    }

    private static bool TryManagedSlice<C1, C2, C3, C4, C5, C6, THandler>(IEntityHost host, EntityDescriptor desc, int from, int to, ComponentOffset<C1> c1, ComponentOffset<C2> c2, ComponentOffset<C3> c3, ComponentOffset<C4> c4, ComponentOffset<C5> c5, ComponentOffset<C6> c6, ref THandler handler)
        where THandler : ISliceHandler<C1, C2, C3, C4, C5, C6>
    {
        var visitor = new ManagedVisitor<C1, C2, C3, C4, C5, C6, THandler>(host, from, to, c1, c2, c3, c4, c5, c6, ref handler);
        desc.HandleType(ref visitor);
        return visitor.Used;
    }

}
