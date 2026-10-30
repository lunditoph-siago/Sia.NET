using Sia.Reactive;

using static Sia.Reactive.Reactive;

namespace Sia.Tests.Reactive;

public class ReactiveErasureTests
{
    [Fact]
    public void ErasedUnit_HasTheSameValueAndHashAsDefault()
    {
        ReactiveNode implicitNode = None;
        var explicitNode = ReactiveNode.Erase(None);
        ReactiveNode empty = default;

        Assert.Equal(empty, implicitNode);
        Assert.Equal(empty, explicitNode);
        Assert.Equal(empty.GetHashCode(), implicitNode.GetHashCode());
        Assert.Equal(empty.GetHashCode(), explicitNode.GetHashCode());
        Assert.NotEqual(empty, (ReactiveNode)Entity(HList.From(1)));
    }

    [Fact]
    public void EmptyAndEntityTransitions_PreserveStateAndEffectLifetime()
    {
        using var world = new World();
        var events = new List<string>();
        var mount = world.Mount(Render, new Props(events, 0));
        var state = mount.GetState<int>();
        state.Set(8);
        world.FlushReactive();

        mount.Update(new Props(events, 1));
        world.FlushReactive();
        Assert.Equal(8, state.Value);
        Assert.Equal(0, world.Query<TypeUnion<Value>>().Count);

        mount.Update(new Props(events, 2));
        world.FlushReactive();
        var output = Assert.Single(world.Query<TypeUnion<Value>>().Hosts.SelectMany(static host => host));
        Assert.Equal(8, output.Get<Value>().Number);

        mount.Update(new Props(events, 0));
        world.FlushReactive();
        Assert.Equal(8, state.Value);
        Assert.Equal(0, world.Query<TypeUnion<Value>>().Count);

        mount.Unmount();
        Assert.Equal(new[] {
            "setup:0", "cleanup:0", "setup:1", "cleanup:1",
            "setup:2", "cleanup:2", "setup:0", "cleanup:0",
        }, events);
        Assert.Throws<ObjectDisposedException>(() => state.Set(9));
    }

    private readonly record struct Props(List<string> Events, int Mode);
    private readonly record struct Value(int Number);

    private static ReactiveNode Render(in Props props, ref Hooks hooks)
    {
        var state = hooks.UseState(7);
        hooks.UseEffect(
            props,
            static (in Props value) => {
                value.Events.Add($"setup:{value.Mode}");
                return value;
            },
            static (in Props value) => value.Events.Add($"cleanup:{value.Mode}"));

        if (props.Mode == 0) {
            return default;
        }
        if (props.Mode == 1) {
            return None;
        }
        return Entity(HList.From(new Value(state.Value)));
    }
}
