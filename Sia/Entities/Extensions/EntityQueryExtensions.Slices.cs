namespace Sia;

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static EntityExtensionsCommon;

public static partial class EntityQueryExtensions
{
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
            else {
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
            else {
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
            else {
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
            else {
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
}
