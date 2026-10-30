using Sia.Reactive;

using static Sia.Reactive.Reactive;

namespace Sia.Tests.Reactive;

public class StateEqualityTests
{
    private readonly record struct Capture(State<int> State, int Command);

    private static ReactiveNode Render(in int initial, ref Hooks hooks)
    {
        hooks.UseState(initial);
        return None;
    }

    [Fact]
    public void Copies_KeepIdentityWhenTheValueChanges()
    {
        using var world = new World();
        var mount = world.Mount(Render, 1);
        var state = mount.GetState<int>();
        var copy = state;
        var hash = state.GetHashCode();

        state.Set(2);
        world.FlushReactive();

        Assert.Equal(2, copy.Value);
        Assert.True(EqualityComparer<State<int>>.Default.Equals(state, copy));
        Assert.Equal(hash, state.GetHashCode());
        Assert.Equal(hash, copy.GetHashCode());
        mount.Unmount();
    }

    [Fact]
    public void DifferentStateCells_WithTheSameValueAreNotEqual()
    {
        using var world = new World();
        var first = world.Mount(Render, 1);
        var second = world.Mount(Render, 1);

        Assert.False(EqualityComparer<State<int>>.Default.Equals(
            first.GetState<int>(), second.GetState<int>()));

        first.Unmount();
        second.Unmount();
    }

    [Fact]
    public void EveryConstructorIdentityField_ParticipatesInEquality()
    {
        using var world = new World();
        using var otherWorld = new World();
        var owner = world.Create(HList.From(1));
        var otherOwner = world.Create(HList.From(2));
        var reconciler = world.AcquireAddon<Reconciler>();
        var otherReconciler = otherWorld.AcquireAddon<Reconciler>();
        var cell = new StateCell<int>();
        var identity = new NodeIdentity(42);
        var state = new State<int>(cell, reconciler, owner, identity);
        var comparer = EqualityComparer<State<int>>.Default;

        Assert.True(comparer.Equals(state, new(cell, reconciler, owner, identity)));
        Assert.False(comparer.Equals(state, new(new StateCell<int>(), reconciler, owner, identity)));
        Assert.False(comparer.Equals(state, new(cell, otherReconciler, owner, identity)));
        Assert.False(comparer.Equals(state, new(cell, reconciler, otherOwner, identity)));
        Assert.False(comparer.Equals(state, new(cell, reconciler, owner, new(43))));
    }

    [Fact]
    public void UnmountedHandles_KeepEqualityAndHashWithoutBecomingUsable()
    {
        using var world = new World();
        var mount = world.Mount(Render, 1);
        var state = mount.GetState<int>();
        var copy = state;
        var hash = state.GetHashCode();

        mount.Unmount();

        Assert.True(EqualityComparer<State<int>>.Default.Equals(state, copy));
        Assert.Equal(hash, state.GetHashCode());
        Assert.Throws<ObjectDisposedException>(() => state.Set(3));
    }

    [Fact]
    public void DefaultHandles_AreEqualAndCanBeHashed()
    {
        State<int> first = default;
        State<int> second = default;

        Assert.True(EqualityComparer<State<int>>.Default.Equals(first, second));
        Assert.True(first.Equals((object)second));
        Assert.False(first.Equals((object)42));
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void EqualRecordCaptures_DoNotAllocateAfterWarmup()
    {
        using var world = new World();
        var mount = world.Mount(Render, 1);
        var first = new Capture(mount.GetState<int>(), 7);
        var second = first;
        for (var index = 0; index < 1024; index++) {
            _ = first == second;
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        var allEqual = true;
        for (var index = 0; index < 1024; index++) {
            allEqual &= first == second;
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(allEqual);
        Assert.Equal(0, allocated);
        mount.Unmount();
    }
}
