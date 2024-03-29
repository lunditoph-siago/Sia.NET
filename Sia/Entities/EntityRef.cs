namespace Sia;

using System.Runtime.CompilerServices;

public readonly record struct EntityRef(in StorageSlot Slot, IEntityHost Host) : IDisposable
{
    public object Boxed => Host.Box(Slot);
    public bool Valid => Host != null && Host.IsValid(Slot);
    public EntityDescriptor Descriptor => Host.Descriptor;

    public readonly Identity Id {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Unsafe.As<byte, Identity>(ref Host.GetByteRef(Slot));
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains<TComponent>()
    {
        try {
            Descriptor.GetOffset<TComponent>();
            return true;
        }
        catch {
            return false;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains(Type componentType)
        => Descriptor.FieldOffsets.ContainsKey(componentType);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe ref TComponent Get<TComponent>()
    {
        try {
            nint offset = Descriptor.GetOffset<TComponent>();
            ref var byteRef = ref Host.GetByteRef(Slot);
            return ref Unsafe.As<byte, TComponent>(
                ref Unsafe.AddByteOffset(ref byteRef, offset));
        }
        catch {
            throw new ComponentNotFoundException("Component not found: " + typeof(TComponent));
        }
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe ref TComponent GetOrNullRef<TComponent>()
    {
        try {
            nint offset = Descriptor.GetOffset<TComponent>();
            ref var byteRef = ref Host.GetByteRef(Slot);
            return ref Unsafe.As<byte, TComponent>(
                ref Unsafe.AddByteOffset(ref byteRef, offset));
        }
        catch {
            return ref Unsafe.NullRef<TComponent>();
        }
    }

    public EntityRef Add<TComponent>()
        => Host.Add(Slot, default(TComponent));

    public EntityRef Add<TComponent>(in TComponent initial)
        => Host.Add(Slot, initial);

    public EntityRef AddMany<TBundle>(in TBundle bundle)
        where TBundle : IHList
        => Host.AddMany(Slot, bundle);

#pragma warning disable CS8500 // This takes the address of, gets the size of, or declares a pointer to a managed type

    private unsafe struct BundleAdder(EntityRef* entity) : IGenericHandler<IHList>
    {
        public readonly void Handle<T>(in T value) where T : IHList
            => *entity = (*entity).AddMany(value);
    }

    public unsafe EntityRef AddBundle<TBundle>(in TBundle bundle)
        where TBundle : IBundle
    {
        EntityRef entity = this;
        bundle.ToHList(new BundleAdder(&entity));
        return entity;
    }

#pragma warning restore CS8500 // This takes the address of, gets the size of, or declares a pointer to a managed type

    public EntityRef Remove<TComponent>()
        => Host.Remove<TComponent>(Slot);

    public void GetHList<THandler>(in StorageSlot slot, in THandler handler)
        where THandler : IRefGenericHandler<IHList>
        => Host.GetHList(slot, handler);

    public Span<byte> AsSpan()
        => Host.GetSpan(Slot);

    public readonly void Dispose()
        => Host.Release(Slot);
}