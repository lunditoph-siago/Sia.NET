namespace Sia.Tests.Entities;

public sealed class TypedDispatchTests
{
    public struct A { public int Value; }
    public struct B { public int Value; }
    public struct C { public int Value; }

    private sealed class Sink(List<string> trace) : IEventSink<Entity, WorldEvents.Add<B>>,
        IEventSink<Entity, WorldEvents.Set<B>>, IEventSink<Entity, WorldEvents.Remove<B>>
    {
        public void OnEvent(Entity target, in WorldEvents.Add<B> e) => trace.Add("add");
        public void OnEvent(Entity target, in WorldEvents.Set<B> e) => trace.Add("set");
        public void OnEvent(Entity target, in WorldEvents.Remove<B> e) => trace.Add("remove");
    }

    private sealed class SensitiveHost<T> : ArrayEntityHost<T>, IEntityHost where T : struct, IHList
    {
        World? IEntityHost.World => throw new InvalidOperationException("Unexpected World access");
    }

    private sealed class RedirectHost<T> : ArrayEntityHost<T> where T : struct, IHList
    {
        public IEntityHost? Destination;
        private readonly struct Mover(Entity entity, RedirectHost<T> owner) : IGenericStructHandler<IHList>
        {
            public void Handle<U>(in U data) where U : struct, IHList
            {
                var source = entity.Host;
                var destination = new SensitiveHost<U>();
                owner.Destination = destination;
                source.MoveOut(entity, destination);
                destination.MoveIn(entity, data, source);
            }
        }

        public override void Add<TComponent>(Entity entity, in TComponent initial)
        {
            var source = entity.Host;
            var destination = new SensitiveHost<HList<TComponent, T>>();
            Destination = destination;
            var moved = HList.Cons(initial, GetRef(entity.Slot));
            source.MoveOut(entity, destination);
            destination.MoveIn(entity, moved, source);
        }

        public override void Remove<TComponent>(Entity entity, out bool success)
            => success = GetRef(entity.Slot).Remove(TypeProxy<TComponent>._, new Mover(entity, this));
    }

    [Fact]
    public void Unrelated_typed_listener_does_not_change_component_operations()
    {
        using var world = new World();
        var entity = world.Create(HList.From(new A { Value = 5 }));
        var unrelated = 0;
        world.Dispatcher.Listen((Entity _, in WorldEvents.Add<C> _) => { unrelated++; return false; });
        entity.Add(new B { Value = 7 });
        Assert.Equal(7, entity.Get<B>().Value);
        entity.Remove<B>();
        Assert.Equal(5, entity.Get<A>().Value);
        Assert.False(entity.Contains<B>());
        Assert.Equal(0, unrelated);
    }

    [Fact]
    public void Typed_add_listener_can_register_set_listener_even_with_unrelated_listener()
    {
        using var world = new World();
        var entity = world.Create(HList.From(new A { Value = 5 }));
        var trace = new List<string>();
        world.Dispatcher.Listen((Entity _, in WorldEvents.Add<C> _) => { trace.Add("unrelated"); return false; });
        world.Dispatcher.Listen((Entity _, in WorldEvents.Add<B> _) => {
            trace.Add("add");
            world.Dispatcher.Listen((Entity _, in WorldEvents.Set<B> _) => { trace.Add("set"); return false; });
            return false;
        });
        entity.Add(new B { Value = 7 });
        Assert.Equal(new[] { "add", "set" }, trace);
    }

    [Fact]
    public void Typed_set_and_remove_listeners_receive_events_when_add_has_no_listener()
    {
        using var world = new World();
        var entity = world.Create(HList.From(new A { Value = 5 }));
        var trace = new List<string>();
        world.Dispatcher.Listen((Entity _, in WorldEvents.Add<C> _) => { trace.Add("unrelated"); return false; });
        world.Dispatcher.Listen((Entity e, in WorldEvents.Set<B> _) => { trace.Add($"set:{e.Get<B>().Value}"); return false; });
        world.Dispatcher.Listen((Entity e, in WorldEvents.Remove<B> _) => { trace.Add($"remove:{e.Contains<B>()}"); return false; });
        entity.Add(new B { Value = 7 });
        entity.Remove<B>();
        Assert.Equal(new[] { "set:7", "remove:False" }, trace);
    }

    [Fact]
    public void Entity_sink_receives_events_alongside_unrelated_typed_listener()
    {
        using var world = new World();
        var entity = world.Create(HList.From(new A { Value = 5 }));
        var trace = new List<string>();
        world.Dispatcher.Listen((Entity _, in WorldEvents.Add<C> _) => { trace.Add("unrelated"); return false; });
        world.Dispatcher.AttachEventSink(entity, new Sink(trace));
        entity.Add(new B { Value = 7 });
        entity.Remove<B>();
        Assert.Equal(new[] { "add", "set", "remove" }, trace);
    }

    [Fact]
    public void Nested_empty_component_events_leave_after_send_action_for_outer_dispatch()
    {
        using var world = new World();
        var entity = world.Create(HList.From(new A { Value = 5 }));
        var trace = new List<string>();
        world.Dispatcher.Listen((Entity _, in WorldEvents.Add<C> _) => {
            trace.Add("outer");
            world.Dispatcher.RunAfterSend(() => trace.Add("after"));
            entity.Add(new B { Value = 7 });
            trace.Add("added");
            return false;
        });
        world.Dispatcher.Send(entity, WorldEvents.Add<C>.Instance);
        Assert.Equal(new[] { "outer", "added", "after" }, trace);
        Assert.Equal(7, entity.Get<B>().Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Empty_dispatch_does_not_read_custom_destination_world(bool unrelatedListener)
    {
        using var world = new World();
        var inner = new RedirectHost<HList<A, EmptyHList>>();
        var host = new WorldEntityHost<HList<A, EmptyHList>, RedirectHost<HList<A, EmptyHList>>>(world, inner);
        world.UnsafeAddRawHost(host);
        var entity = host.Create(HList.From(new A { Value = 5 }));
        if (unrelatedListener) world.Dispatcher.Listen((Entity _, in WorldEvents.Add<C> _) => false);
        try {
            entity.Add(new B { Value = 7 });
            Assert.Equal(7, entity.Get<B>().Value);
        }
        finally { inner.Destination?.Dispose(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Empty_remove_dispatch_does_not_read_custom_destination_world(bool unrelatedListener)
    {
        using var world = new World();
        var inner = new RedirectHost<HList<B, HList<A, EmptyHList>>>();
        var host = new WorldEntityHost<HList<B, HList<A, EmptyHList>>, RedirectHost<HList<B, HList<A, EmptyHList>>>>(world, inner);
        world.UnsafeAddRawHost(host);
        var entity = host.Create(HList.From(new B { Value = 7 }, new A { Value = 5 }));
        if (unrelatedListener) world.Dispatcher.Listen((Entity _, in WorldEvents.Add<C> _) => false);
        try {
            entity.Remove<B>();
            Assert.False(entity.Contains<B>());
            Assert.Equal(5, entity.Get<A>().Value);
        }
        finally { inner.Destination?.Dispose(); }
    }
}
