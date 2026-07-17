namespace Sia;

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using MemoryPack;

[MemoryPackable(GenerateType.NoGenerate)]
public readonly partial struct Entity : IEquatable<Entity>
{
    private readonly EntityState? _state;
    private readonly ulong _token;

    internal Entity(EntityState state, uint generation, EntityId id)
    {
        _state = state;
        _token = ((ulong)generation << 32) | (uint)id.Value;
        state.Token = _token;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal Entity(EntityState state)
    {
        _state = state;
        _token = state.Token;
    }

    public EntityId Id {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => new(unchecked((int)_token));
    }

    public IEntityHost Host {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get {
            var state = _state;
            return state != null
                && state.Token == _token
                && state.Host != null
                ? state.Host
                : null!;
        }
    }

    public int Slot {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => GetState().Slot;
    }

    public object Boxed {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get {
            var state = GetState();
            return state.Host!.Box(state.Slot);
        }
    }

    public bool IsValid {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get {
            var state = _state;
            return state != null
                && state.Token == _token
                && state.Host != null;
        }
    }

    public EntityDescriptor Descriptor {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => GetState().Host!.Descriptor;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal EntityState GetCurrentState()
    {
        var state = _state;
        if (state == null || state.Token != _token) {
            ThrowDisposed();
        }
        return state;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal EntityState GetState()
    {
        var state = GetCurrentState();
        if (state.Host == null) {
            ThrowDisposed();
        }
        return state;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    [DoesNotReturn]
    private readonly void ThrowDisposed()
        => throw new ObjectDisposedException(ToString());

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal EntityState GetStateUnchecked()
        => _state!;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool References(EntityState state)
        => ReferenceEquals(_state, state) && _token == state.Token;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool ContainsUnchecked<TComponent>()
        => _state!.Host!.Descriptor.Contains<TComponent>();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ref TComponent GetUnchecked<TComponent>()
    {
        var state = _state!;
        var host = state.Host!;
        ref var byteRef = ref host.GetByteRef(state.Slot);
        nint offset = host.Descriptor.GetOffsetUnchecked<TComponent>();
        return ref Unsafe.As<byte, TComponent>(
            ref Unsafe.AddByteOffset(ref byteRef, offset));
    }

    public void Destroy()
    {
        var state = GetState();
        state.Host!.Release(this);
    }

    public override string ToString()
        => "[Entity " + Id + "]";

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(Entity other)
        => ReferenceEquals(_state, other._state) && _token == other._token;

    public override bool Equals(object? obj)
        => obj is Entity other && Equals(other);

    public override int GetHashCode()
        => Id.Value;

    public static bool operator ==(Entity left, Entity right)
        => left.Equals(right);

    public static bool operator !=(Entity left, Entity right)
        => !left.Equals(right);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains<TComponent>() => Descriptor.Contains<TComponent>();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains(Type componentType)
        => Descriptor.Offsets.ContainsKey(componentType);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref TComponent Get<TComponent>()
    {
        var state = GetState();
        var host = state.Host!;
        ref var byteRef = ref host.GetByteRef(state.Slot);
        nint offset = host.Descriptor.GetOffset<TComponent>();
        return ref Unsafe.As<byte, TComponent>(
            ref Unsafe.AddByteOffset(ref byteRef, offset));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref TComponent GetOrNullRef<TComponent>()
    {
        var state = GetState();
        var host = state.Host!;
        ref var byteRef = ref host.GetByteRef(state.Slot);
        try {
            nint offset = host.Descriptor.GetOffset<TComponent>();
            return ref Unsafe.As<byte, TComponent>(
                ref Unsafe.AddByteOffset(ref byteRef, offset));
        }
        catch {
            return ref Unsafe.NullRef<TComponent>();
        }
    }

    public Entity Add<TComponent>()
    {
        GetState().Host!.Add(this, default(TComponent));
        return this;
    }

    public Entity Add<TComponent>(in TComponent initial)
    {
        GetState().Host!.Add(this, initial);
        return this;
    }

    public Entity AddMany<TList>(in TList bundle)
        where TList : struct, IHList
    {
        GetState().Host!.AddMany(this, bundle);
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
        GetState().Host!.Set(this, value);
        return this;
    }

    public Entity Remove<TComponent>()
    {
        GetState().Host!.Remove<TComponent>(this, out _);
        return this;
    }

    public Entity Remove<TComponent>(out bool success)
    {
        GetState().Host!.Remove<TComponent>(this, out success);
        return this;
    }

    public Entity RemoveMany<TList>()
        where TList : struct, IHList
    {
        GetState().Host!.RemoveMany<TList>(this);
        return this;
    }

    private struct BundleRemover(Entity entity)
        : IGenericStructTypeHandler<IHList>
    {
        public readonly void Handle<T>()
            where T : struct, IHList
            => entity.RemoveMany<T>();
    }

    public Entity RemoveBundle<TBundle>()
        where TBundle : IStaticBundle
    {
        var entity = this;
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
    {
        var state = GetState();
        state.Host!.GetHList(state.Slot, handler);
    }

    public Span<byte> AsSpan()
    {
        var state = GetState();
        return state.Host!.GetBytes(state.Slot);
    }
}
