namespace Sia;

using System.Runtime.CompilerServices;

public readonly record struct EntityRef(scoped in StorageSlot Slot, IEntityHost Host)
    : IDisposable
{
    public object Boxed => Host.Box(Slot);
    public bool Valid => Host != null && Host.IsValid(Slot);
    public EntityDescriptor Descriptor => Host.GetDescriptor(Slot);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public EntityRef<TEntity> Cast<TEntity>()
        where TEntity : struct
    {
        var descriptor = Host.GetDescriptor(Slot);
        if (typeof(TEntity) != descriptor.Type) {
            throw new InvalidCastException(
                $"Cannot cast entity type from {descriptor.Type} to {typeof(TEntity)}");
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
    public unsafe ref TComponent Get<TComponent>()
    {
        ref var entityRef = ref Host.GetSpan(Slot)[0];
        nint offset = Descriptor.GetOffset<TComponent>();
        if (offset == -1) {
            throw new ComponentNotFoundException("Component not found: " + typeof(TComponent));
        }
        return ref Unsafe.AsRef<TComponent>(
            (void*)((IntPtr)Unsafe.AsPointer(ref entityRef) + offset));
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe ref TComponent GetOrNullRef<TComponent>()
    {
        ref var entityRef = ref Host.GetSpan(Slot)[0];
        nint offset = Descriptor.GetOffset<TComponent>();
        if (offset == -1) {
            return ref Unsafe.NullRef<TComponent>();
        }
        return ref Unsafe.AsRef<TComponent>(
            (void*)((IntPtr)Unsafe.AsPointer(ref entityRef) + offset));
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
    public EntityDescriptor Descriptor => Host.GetDescriptor(Slot);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe ref TEntity AsRef()
        => ref Unsafe.AsRef<TEntity>(Unsafe.AsPointer(ref AsSpan()[0]));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe EntityRef<Bundle<TEntity, TComponent>> Add<TComponent>(
        in TComponent newComponent, IEntityCreator creator)
    {
        var bundle = Bundle.Create(AsRef(), newComponent);
        return creator.CreateEntity(bundle);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe EntityRef<TEntity> Move(IEntityCreator creator)
    {
        var data = AsRef();
        Dispose();
        return creator.CreateEntity(data);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe EntityRef<Bundle<TEntity, TComponent>> Move<TComponent>(
        in TComponent newComponent, IEntityCreator creator)
    {
        var bundle = Bundle.Create(AsRef(), newComponent);
        Dispose();
        return creator.CreateEntity(bundle);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains<TComponent>()
        => Descriptor.GetOffset<TComponent>() != -1;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains(Type componentType)
        => Descriptor.GetOffset(componentType) != -1;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe ref TComponent Get<TComponent>()
    {
        ref var entityRef = ref Host.GetSpan(Slot)[0];
        nint offset = Descriptor.GetOffset<TComponent>();
        if (offset == -1) {
            throw new ComponentNotFoundException("Component not found: " + typeof(TComponent));
        }
        return ref Unsafe.AsRef<TComponent>(
            (void*)((IntPtr)Unsafe.AsPointer(ref entityRef) + offset));
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe ref TComponent GetOrNullRef<TComponent>()
    {
        ref var entityRef = ref Host.GetSpan(Slot)[0];
        nint offset = Descriptor.GetOffset<TComponent>();
        if (offset == -1) {
            return ref Unsafe.NullRef<TComponent>();
        }
        return ref Unsafe.AsRef<TComponent>(
            (void*)((IntPtr)Unsafe.AsPointer(ref entityRef) + offset));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<byte> AsSpan()
        => Host.GetSpan(Slot);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly void Dispose()
        => Host.Release(Slot);
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator EntityRef(in EntityRef<TEntity> entity)
        => new(entity.Slot, entity.Host);
}