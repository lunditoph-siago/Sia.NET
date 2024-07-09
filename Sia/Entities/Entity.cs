namespace Sia;

using System.Runtime.CompilerServices;
using MemoryPack;
using Microsoft.Extensions.ObjectPool;

[MemoryPackable(GenerateType.NoGenerate)]
public partial record Entity
{
    public static readonly ObjectPool<Entity> Pool = new DefaultObjectPool<Entity>(new PooledEntityPolicy());

    public EntityId Id { get; } = EntityId.Create();
    public IEntityHost Host { get; internal set; } = null!;
    public int Slot { get; internal set; }

    public object Boxed => Host.Box(Slot);
    public bool IsValid => Host != null;
    public EntityDescriptor Descriptor => Host.Descriptor;

    private class PooledEntityPolicy : IPooledObjectPolicy<Entity>
    {
        public Entity Create() => new();
        public bool Return(Entity obj) => true;
    }

    private Entity() {}
    public void Destroy() => Host.Release(this);

    public override string ToString()
        => "[Entity " + Id + "]";

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains<TComponent>() => Descriptor.Contains<TComponent>();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains(Type componentType)
        => Descriptor.Offsets.ContainsKey(componentType);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref TComponent Get<TComponent>()
    {
        ref var byteRef = ref Host.GetByteRef(Slot);
        nint offset = Descriptor.GetOffset<TComponent>();
        return ref Unsafe.As<byte, TComponent>(
            ref Unsafe.AddByteOffset(ref byteRef, offset));
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref TComponent GetOrNullRef<TComponent>()
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

    public Entity Add<TComponent>()
    {
        Host.Add(this, default(TComponent));
        return this;
    }

    public Entity Add<TComponent>(in TComponent initial)
    {
        Host.Add(this, initial);
        return this;
    }

    public Entity AddMany<TList>(in TList bundle)
        where TList : struct, IHList
    {
        Host.AddMany(this, bundle);
        return this;
    }

    private struct BundleAdder(Entity entity) : IGenericStructHandler<IHList>
    {
        public readonly void Handle<T>(in T value)
            where T : struct, IHList
            => entity.AddMany(value);
    }

    public Entity AddBundle<TBundle>(in TBundle bundle)
        where TBundle : IBundle
    {
        bundle.ToHList(new BundleAdder(this));
        return this;
    }

    public Entity Set<TComponent>(in TComponent value)
    {
        Host.Set(this, value);
        return this;
    }

    public Entity Remove<TComponent>()
    {
        Host.Remove<TComponent>(this, out _);
        return this;
    }

    public Entity Remove<TComponent>(out bool success)
    {
        Host.Remove<TComponent>(this, out success);
        return this;
    }

    public Entity RemoveMany<TList>()
        where TList : struct, IHList
    {
        Host.RemoveMany<TList>(this);
        return this;
    }

    private unsafe struct BundleRemover(Entity entity)
        : IGenericStructTypeHandler<IHList>
    {
        public readonly void Handle<T>()
            where T : struct, IHList
            => entity.RemoveMany<T>();
    }

    public Entity RemoveBundle<TBundle>()
        where TBundle : IStaticBundle
    {
        Entity entity = this;
        TBundle.StaticHandleHListType(new BundleRemover(entity));
        return entity;
    }

    public Entity RemoveBundle<TBundle>(in TBundle bundle)
        where TBundle : IBundle
    {
        bundle.HandleHListType(new BundleRemover(this));
        return this;
    }

    public void GetHList<THandler>(in THandler handler)
        where THandler : IRefGenericHandler<IHList>
        => Host.GetHList(Slot, handler);

    public Span<byte> AsSpan()
        => Host.GetBytes(Slot);
}