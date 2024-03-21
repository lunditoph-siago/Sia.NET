namespace Sia;

using System.Runtime.CompilerServices;

public readonly record struct EntityRef(in StorageSlot Slot, IEntityHost Host) : IDisposable
{
    public object Boxed => Host.Box(Slot);
    public bool Valid => Host != null && Host.IsValid(Slot);
    public EntityDescriptor Descriptor => Host.Descriptor;
    
    public bool Contains<TComponent>()
        => Descriptor.GetOffset<TComponent>() != -1;

    public bool Contains(Type componentType)
        => Descriptor.GetOffset(componentType) != -1;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe ref TComponent Get<TComponent>()
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
    public unsafe ref TComponent GetOrNullRef<TComponent>()
    {
        nint offset = Descriptor.GetOffset<TComponent>();
        if (offset == -1) {
            return ref Unsafe.NullRef<TComponent>();
        }
        ref var byteRef = ref Host.GetByteRef(Slot);
        return ref Unsafe.As<byte, TComponent>(
            ref Unsafe.AddByteOffset(ref byteRef, offset));
    }

    public EntityRef Add<TComponent>()
        => Host.Add(Slot, default(TComponent));

    public EntityRef Add<TComponent>(in TComponent initial)
        => Host.Add(Slot, initial);

    public EntityRef AddMany<TBundle>(in TBundle bundle)
        where TBundle : IHList
        => Host.AddMany(Slot, bundle);

    public void AddMany<TBundle>(in TBundle bundle)
        where TBundle : IBundle
        => bundle.ToMany(new BundleCreateHandler(this));

    public EntityRef Remove<TComponent>()
        => Host.Remove<TComponent>(Slot);

    public Span<byte> AsSpan()
        => Host.GetSpan(Slot);

    public readonly void Dispose()
        => Host.Release(Slot);
    
    private readonly struct BundleCreateHandler(EntityRef entityRef): IGenericHandler<IHList>
    {
        public void Handle<T>(in T value)
            where T : IBundle
            => entityRef.AddMany(value);
    }
}