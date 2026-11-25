using System.Runtime.CompilerServices;

namespace Sia;

public sealed class WorldDispatcher : Dispatcher<IEvent>
{
    public World World { get; }

    internal WorldDispatcher(World world)
    {
        World = world;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Send<UEvent>(Entity target, in UEvent e)
        => Send(target, e, owned: false);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void Send<UEvent>(Entity target, in UEvent e, bool owned) where UEvent : IEvent
    {
        World.EnsureOwnerThread();
        var state = target.IsValid ? target.GetStateUnchecked() : default;
        var owner = state.EventSink;
        if (!owned) {
            if (owner != null && !ReferenceEquals(state.Host!.World, World)) {
                throw new ArgumentException("The entity belongs to a different world.", nameof(target));
            }
            if (owner is null && HasNoListeners<UEvent>()) return;
        }
        SendToListeners(target, e, owner as IEventSink<Entity, UEvent>);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal void SendAddAndSet<TComponent>(Entity target)
    {
        if (!target.IsValid) return;
        var add = WorldEvents.Add<TComponent>.Instance;
        World.EnsureOwnerThread();
        var state = target.IsValid ? target.GetStateUnchecked() : default;
        var owner = state.EventSink;
        if (owner != null && !ReferenceEquals(state.Host!.World, World)) {
            throw new ArgumentException("The entity belongs to a different world.", nameof(target));
        }
        if (owner is null && HasNoListeners<WorldEvents.Add<TComponent>>()) {
            var set = WorldEvents.Set<TComponent>.Instance;
            World.EnsureOwnerThread();
            state = target.IsValid ? target.GetStateUnchecked() : default;
            if (state.EventSink is null && HasNoListeners<WorldEvents.Set<TComponent>>()) return;
            Send(target, set);
            return;
        }
        SendToListeners(target, add, owner as IEventSink<Entity, WorldEvents.Add<TComponent>>);
        if (target.IsValid) {
            Send(target, WorldEvents.Set<TComponent>.Instance);
        }
    }

    public void AttachEventSink(Entity target, IEventSink<Entity> owner)
    {
        World.EnsureOwnerThread();
        ArgumentNullException.ThrowIfNull(owner);
        var state = target.GetState();
        if (!ReferenceEquals(state.Host!.World, World)) {
            throw new ArgumentException("Foreign entity.", nameof(target));
        }
        if (state.EventSink != null) {
            throw new InvalidOperationException("Entity already has an event owner.");
        }
        state.EventSink = owner;
    }

    public override void UnlistenAll(Entity target)
    {
        if (!World.IsDisposed && World.Actor is not { IsCompleted: true }) {
            World.EnsureOwnerThread();
        }
        DetachEntityListeners(target);
    }

    internal void DetachEntityListeners(Entity target)
    {
        if (IsSending || !HasNoUntypedListeners) base.UnlistenAll(target);
        if (target.GetStateUnchecked() is { EventSink: not null, Host: { } host } state
            && target.References(state) && ReferenceEquals(host.World, World)) {
            state.EventSink = null;
        }
    }

    public override void Clear()
    {
        World.EnsureOwnerThread();
        base.Clear();
        foreach (var host in World.Hosts) {
            foreach (var entity in host.UnsafeGetEntitySpan()) {
                var state = entity.GetStateUnchecked();
                state.EventSink = null;
            }
        }
    }
}
