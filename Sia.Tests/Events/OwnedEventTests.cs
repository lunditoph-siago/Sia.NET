namespace Sia.Tests.Events;

public sealed class OwnedEventTests
{
    private readonly record struct Ping : IEvent;
    private readonly record struct Pong : IEvent;
    private sealed class PairSink(List<string> trace) : IEventSink<Entity, Ping>, IEventSink<Entity, Pong>
    {
        public void OnEvent(Entity target, in Ping e) => trace.Add("ping");
        public void OnEvent(Entity target, in Pong e) => trace.Add("pong");
    }
    private sealed class Sink(Action<Entity> action) : IEventSink<Entity, Ping>
    {
        public void OnEvent(Entity target, in Ping e) => action(target);
    }
    private sealed class Observer(Action<Entity> action) : IEventListener<Entity>
    {
        public bool OnEvent<TEvent>(Entity target, in TEvent e) where TEvent : IEvent
        {
            if (typeof(TEvent) == typeof(Ping))
                action(target);
            return false;
        }
    }

    [Fact]
    public void OwnedEvents_DispatchDeclaredTypesAndPreserveObservers()
    {
        using var world = new World();
        var entity = world.Create(HList.From(1));
        var trace = new List<string>();
        world.Dispatcher.AttachEventSink(entity, new PairSink(trace));
        var sets = 0;
        world.Dispatcher.Listen<WorldEvents.Set<int>>((Entity _, in WorldEvents.Set<int> e) => { sets++; return false; });
        world.Send(entity, new Pong());
        world.Send(entity, WorldEvents.Set<int>.Instance);
        world.Send(entity, new Ping());
        Assert.Equal(new[] { "pong", "ping" }, trace);
        Assert.Equal(1, sets);
    }

    [Fact]
    public void InterfaceDispatch_PreservesObserverAndOwnerOrder()
    {
        using var world = new World();
        var entity = world.Create(HList.From(1));
        var trace = new List<string>();
        world.Dispatcher.Listen(new Observer(_ => trace.Add("global")));
        world.Dispatcher.Listen<Ping>((Entity _, in Ping e) => { trace.Add("type"); return false; });
        world.Dispatcher.Listen(entity, new Observer(_ => trace.Add("target")));
        world.Dispatcher.AttachEventSink(entity, new Sink(_ => {
            trace.Add("owner");
            Assert.True(world.Dispatcher.IsSending);
            Assert.Throws<InvalidOperationException>(() => world.Dispatcher.UnlistenAll(entity));
            world.Dispatcher.RunAfterSend(() => trace.Add("after"));
        }));
        IEventSender<Entity, IEvent> sender = world.Dispatcher;
        sender.Send(entity, new Ping());
        Assert.Equal(new[] { "global", "type", "owner", "target", "after" }, trace);
    }

    [Fact]
    public void EventSink_SurvivesCompactionWithoutLeakingToRecycledIdentity()
    {
        using var world = new World();
        var first = world.Create(HList.From(0));
        var entity = world.Create(HList.From(1));
        var calls = 0;
        world.Dispatcher.AttachEventSink(entity, new Sink(_ => calls++));
        first.Destroy();
        world.Send(entity, new Ping());
        Assert.Equal(1, calls);
        entity.Destroy();
        var recycled = world.Create(HList.From(2));
        world.Send(recycled, new Ping());
        world.Dispatcher.Send(entity, new Ping());
        Assert.Equal(1, calls);
        Assert.Throws<ObjectDisposedException>(() => world.Dispatcher.AttachEventSink(entity, new Sink(_ => calls++)));
    }

    [Fact]
    public void UnlistenAllAndClear_DetachEventSinks()
    {
        using var world = new World();
        var a = world.Create(HList.From(1));
        var b = world.Create(HList.From(2));
        var calls = 0;
        var sink = new Sink(_ => calls++);
        world.Dispatcher.AttachEventSink(a, sink);
        world.Dispatcher.AttachEventSink(b, sink);
        Assert.Throws<InvalidOperationException>(() => world.Dispatcher.AttachEventSink(a, sink));
        world.Dispatcher.UnlistenAll(a);
        world.Send(a, new Ping());
        world.Send(b, new Ping());
        Assert.Equal(1, calls);
        world.Dispatcher.Clear();
        world.Send(b, new Ping());
        Assert.Equal(1, calls);
        world.Dispatcher.AttachEventSink(b, sink);
        world.Send(b, new Ping());
        Assert.Equal(2, calls);
    }

    [Fact]
    public void EventSinkFailure_UnwindsDispatchAndRunsDeferredWork()
    {
        using var world = new World();
        var entity = world.Create(HList.From(1));
        var deferred = false;
        world.Dispatcher.AttachEventSink(entity, new Sink(_ => {
            world.Dispatcher.RunAfterSend(() => deferred = true);
            throw new InvalidOperationException("owner failed");
        }));
        Assert.Throws<InvalidOperationException>(() => world.Send(entity, new Ping()));
        Assert.True(deferred);
        Assert.False(world.Dispatcher.IsSending);
        world.Dispatcher.UnlistenAll(entity);
    }

    [Fact]
    public void AttachEventSink_RejectsForeignWorld()
    {
        using var a = new World();
        using var b = new World();
        var entity = a.Create(HList.From(1));
        Assert.Throws<ArgumentException>(() => b.Dispatcher.AttachEventSink(entity, new Sink(_ => {})));
    }
}
