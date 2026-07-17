namespace Sia;

using System.Runtime.CompilerServices;

internal readonly struct EntityReference(Entity entity)
{
    private readonly EntityState? _state = entity.GetState();

    public bool IsValid {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _state is { Host: not null };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGet(out Entity entity)
    {
        var state = _state;
        if (state == null || state.Host == null) {
            entity = default;
            return false;
        }
        entity = new Entity(state);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Entity GetOrDefault()
        => TryGet(out var entity) ? entity : default;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Entity GetUnchecked()
        => new(_state!);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ContainsUnchecked<TComponent>()
        => _state!.Host!.Descriptor.Contains<TComponent>();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref TComponent GetUnchecked<TComponent>()
    {
        var state = _state!;
        var host = state.Host!;
        ref var byteRef = ref host.GetByteRef(state.Slot);
        nint offset = host.Descriptor.GetOffsetUnchecked<TComponent>();
        return ref Unsafe.As<byte, TComponent>(
            ref Unsafe.AddByteOffset(ref byteRef, offset));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Matches(Entity entity)
        => _state is { } state && entity.References(state);
}
