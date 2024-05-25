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
        => Descriptor.Offsets.ContainsKey(componentType);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe ref TComponent Get<TComponent>()
    {
        ref var byteRef = ref Host.GetByteRef(Slot);
        nint offset = Descriptor.GetOffset<TComponent>();
        return ref Unsafe.As<byte, TComponent>(
            ref Unsafe.AddByteOffset(ref byteRef, offset));
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe ref TComponent GetOrNullRef<TComponent>()
    {
        ref var byteRef = ref Host.GetByteRef(Slot);
        try {
            nint offset = Descriptor.GetOffset<TComponent>();
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

    public EntityRef AddMany<TList>(in TList bundle)
        where TList : IHList
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

    public EntityRef Set<TComponent>(in TComponent value)
        => Host.Set(Slot, value);

    public EntityRef Remove<TComponent>()
        => Host.Remove<TComponent>(Slot);

    public EntityRef RemoveMany<TList>()
        where TList : IHList
        => Host.RemoveMany<TList>(Slot);

#pragma warning disable CS8500 // This takes the address of, gets the size of, or declares a pointer to a managed type

    private unsafe struct BundleRemover(EntityRef* entity) : IGenericTypeHandler<IHList>
    {
        public readonly void Handle<T>() where T : IHList
            => *entity = (*entity).RemoveMany<T>();
    }

    public unsafe EntityRef RemoveBundle<TBundle>()
        where TBundle : IStaticBundle
    {
        EntityRef entity = this;
        TBundle.StaticHandleHListType(new BundleRemover(&entity));
        return entity;
    }

    public unsafe EntityRef RemoveBundle<TBundle>(in TBundle bundle)
        where TBundle : IBundle
    {
        EntityRef entity = this;
        bundle.HandleHListType(new BundleRemover(&entity));
        return entity;
    }

#pragma warning restore CS8500 // This takes the address of, gets the size of, or declares a pointer to a managed type

    public void GetHList<THandler>(in THandler handler)
        where THandler : IRefGenericHandler<IHList>
        => Host.GetHList(Slot, handler);

    public Span<byte> AsSpan()
        => Host.GetSpan(Slot);

    public readonly void Dispose()
        => Host.Release(Slot);
}