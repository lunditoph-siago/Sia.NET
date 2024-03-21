namespace Sia;

using System.Runtime.CompilerServices;

public readonly struct ComponentGetter<TComponent>(EntityDescriptor descriptor)
{
    public readonly nint Offset = descriptor.GetOffset<TComponent>();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref TComponent Get(IEntityHost host, in StorageSlot slot)
    {
        ref var byteRef = ref host.GetByteRef(slot);
        return ref UnsafeGet(ref byteRef);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref TComponent UnsafeGet(IEntityHost host, in StorageSlot slot)
    {
        ref var byteRef = ref host.UnsafeGetByteRef(slot);
        return ref UnsafeGet(ref byteRef);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref TComponent UnsafeGet(ref byte entity)
        => ref Unsafe.As<byte, TComponent>(ref Unsafe.AddByteOffset(ref entity, Offset));
}