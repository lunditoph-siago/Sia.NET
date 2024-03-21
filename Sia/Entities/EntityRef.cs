namespace Sia;

using System.Runtime.CompilerServices;

public readonly record struct EntityRef(scoped in StorageSlot Slot, IEntityHost Host) : IDisposable
{
    public object Boxed => Host.Box(Slot);
    public bool Valid => Host != null && Host.IsValid(Slot);
    public EntityDescriptor Descriptor => Host.Descriptor;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public EntityRef<TEntity> Cast<TEntity>()
        where TEntity : struct
    {
        if (typeof(TEntity) != Descriptor.Type) {
            throw new InvalidCastException(
                $"Cannot cast entity type from {Descriptor.Type} to {typeof(TEntity)}");
        }
        return new(Slot, Host);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public EntityRef<TEntity> UnsafeCast<TEntity>()
        where TEntity : struct
        => new(Slot, Host);
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains<TComponent>()
        => Descriptor.GetOffset<TComponent>() != -1;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains(Type componentType)
        => Descriptor.GetOffset(componentType) != -1;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref TComponent Get<TComponent>()
    {
        nint offset = Descriptor.GetOffset<TComponent>();
        if (offset == -1) {
            throw new ComponentNotFoundException("Component not found: " + typeof(TComponent));
        }
        ref var byteRef = ref Host.GetByteRef(Slot);
        return ref Unsafe.As<byte, TComponent>(
            ref Unsafe.AddByteOffset(ref byteRef, offset));
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref TComponent GetOrNullRef<TComponent>()
    {
        nint offset = Descriptor.GetOffset<TComponent>();
        if (offset == -1) {
            return ref Unsafe.NullRef<TComponent>();
        }
        ref var byteRef = ref Host.GetByteRef(Slot);
        return ref Unsafe.As<byte, TComponent>(
            ref Unsafe.AddByteOffset(ref byteRef, offset));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<byte> AsSpan()
        => Host.GetSpan(Slot);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly void Dispose()
        => Host.Release(Slot);
}

public readonly record struct EntityRef<TEntity>(scoped in StorageSlot Slot, IEntityHost Host)
    : IDisposable
    where TEntity : struct
{
    public object Boxed => Host.Box(Slot);
    public bool Valid => Host != null && Host.IsValid(Slot);
    public EntityDescriptor Descriptor => Host.Descriptor;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe ref TEntity AsRef()
        => ref Unsafe.AsRef<TEntity>(Unsafe.AsPointer(ref Host.GetByteRef(Slot)));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe ref TEntity UnsafeAsRef()
        => ref Unsafe.AsRef<TEntity>(Unsafe.AsPointer(ref Host.UnsafeGetByteRef(Slot)));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains<TComponent>()
        => Descriptor.GetOffset<TComponent>() != -1;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains(Type componentType)
        => Descriptor.GetOffset(componentType) != -1;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref TComponent Get<TComponent>()
    {
        nint offset = Descriptor.GetOffset<TComponent>();
        if (offset == -1) {
            throw new ComponentNotFoundException("Component not found: " + typeof(TComponent));
        }
        ref var byteRef = ref Host.GetByteRef(Slot);
        return ref Unsafe.As<byte, TComponent>(
            ref Unsafe.AddByteOffset(ref byteRef, offset));
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref TComponent GetOrNullRef<TComponent>()
    {
        nint offset = Descriptor.GetOffset<TComponent>();
        if (offset == -1) {
            return ref Unsafe.NullRef<TComponent>();
        }
        ref var byteRef = ref Host.GetByteRef(Slot);
        return ref Unsafe.As<byte, TComponent>(
            ref Unsafe.AddByteOffset(ref byteRef, offset));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<byte> AsSpan()
        => Host.GetSpan(Slot);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose()
        => Host.Release(Slot);
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator EntityRef(in EntityRef<TEntity> entity)
        => new(entity.Slot, entity.Host);
}