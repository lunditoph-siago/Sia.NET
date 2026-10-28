namespace Sia.Tests.Entities;

public sealed class StructuralDispatchTests
{
    public struct A { public int Value; }
    public struct B { public int Value; }

    private sealed class Sink(List<string> trace) : IEventSink<Entity, WorldEvents.Add<B>>,
        IEventSink<Entity, WorldEvents.Set<B>>, IEventSink<Entity, WorldEvents.Remove<B>>
    {
        public void OnEvent(Entity entity, in WorldEvents.Add<B> e) => trace.Add($"add:{entity.Get<B>().Value}");
        public void OnEvent(Entity entity, in WorldEvents.Set<B> e) => trace.Add($"set:{entity.Get<B>().Value}");
        public void OnEvent(Entity entity, in WorldEvents.Remove<B> e) => trace.Add($"remove:{entity.Contains<B>()}");
    }

    private sealed class Listener(List<string> trace) : IEventListener
    {
        public bool OnEvent<T>(Entity target, in T e) where T : IEvent
        {
            if (e is WorldEvents.Add<B>) trace.Add("add");
            if (e is WorldEvents.Set<B>) trace.Add("set");
            if (e is WorldEvents.Remove<B>) trace.Add("remove");
            return false;
        }
    }

    [Fact]
    public void Attached_sink_receives_component_events_without_dispatcher_listeners()
    {
        using var world = new World();
        var entity = world.Create(HList.From(new A { Value = 5 }));
        var trace = new List<string>();
        world.Dispatcher.AttachEventSink(entity, new Sink(trace));
        entity.Add(new B { Value = 7 });
        entity.Remove<B>();
        Assert.Equal(new[] { "add:7", "set:7", "remove:False" }, trace);
    }

    [Fact]
    public void Move_callback_can_register_listener_before_component_events()
    {
        using var world = new World();
        var source = world.GetArrayHost<HList<A, EmptyHList>>();
        var entity = source.Create(HList.From(new A { Value = 5 }));
        var trace = new List<string>();
        source.OnEntityMovedOut += (_, _) => {
            world.Dispatcher.Listen((Entity _, in WorldEvents.Add<B> _) => { trace.Add("add"); return false; });
            world.Dispatcher.Listen((Entity _, in WorldEvents.Set<B> _) => { trace.Add("set"); return false; });
        };
        entity.Add(new B { Value = 7 });
        Assert.Equal(new[] { "add", "set" }, trace);
    }

    [Fact]
    public void Move_callback_can_attach_sink_before_component_events()
    {
        using var world = new World();
        var source = world.GetArrayHost<HList<A, EmptyHList>>();
        var destination = world.GetArrayHost<HList<B, HList<A, EmptyHList>>>();
        var entity = source.Create(HList.From(new A { Value = 5 }));
        var trace = new List<string>();
        destination.OnEntityMovedIn += (moved, _) => world.Dispatcher.AttachEventSink(moved, new Sink(trace));
        entity.Add(new B { Value = 7 });
        entity.Remove<B>();
        Assert.Equal(new[] { "add:7", "set:7", "remove:False" }, trace);
    }

    [Fact]
    public void Add_listener_can_register_set_listener_and_queue_action()
    {
        using var world = new World();
        var entity = world.Create(HList.From(new A { Value = 5 }));
        var trace = new List<string>();
        world.Dispatcher.Listen((Entity _, in WorldEvents.Add<B> _) => {
            trace.Add("add");
            world.Dispatcher.Listen((Entity _, in WorldEvents.Set<B> _) => { trace.Add("set"); return false; });
            world.Dispatcher.RunAfterSend(() => trace.Add("after-add"));
            return false;
        });
        entity.Add(new B { Value = 7 });
        Assert.Equal(new[] { "add", "after-add", "set" }, trace);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Global_and_target_listeners_receive_all_component_events(bool targetOnly)
    {
        using var world = new World();
        var entity = world.Create(HList.From(new A { Value = 5 }));
        var trace = new List<string>();
        var listener = new Listener(trace);
        if (targetOnly) world.Dispatcher.Listen(entity, listener);
        else world.Dispatcher.Listen(listener);
        entity.Add(new B { Value = 7 });
        entity.Remove<B>();
        Assert.Equal(new[] { "add", "set", "remove" }, trace);
    }

    [Fact]
    public void Throwing_add_listener_stops_set_event_but_leaves_component_added()
    {
        using var world = new World();
        var entity = world.Create(HList.From(new A { Value = 5 }));
        var setCalled = false;
        world.Dispatcher.Listen((Entity _, in WorldEvents.Add<B> _) => throw new InvalidOperationException("add"));
        world.Dispatcher.Listen((Entity _, in WorldEvents.Set<B> _) => { setCalled = true; return false; });
        Assert.Throws<InvalidOperationException>(() => entity.Add(new B { Value = 7 }));
        Assert.True(entity.Contains<B>());
        Assert.False(setCalled);
    }
}
