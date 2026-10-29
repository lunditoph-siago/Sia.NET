namespace Sia.Tests.Entities;

public sealed class WorldEntityHostBatchTypedTests
{
    public struct A { public int Value; }
    public struct B { public int Value; }
    public struct C { public int Value; }
    public struct D { public int Value; }
    public sealed class SyntheticEvent<T,U> : IEvent;

    [Fact]
    public void Unrelated_typed_listener_is_not_called_and_values_survive_roundtrip()
    {
        using var world = new World();
        var source = world.GetArrayHost<HList<A, EmptyHList>>();
        var target = world.GetArrayHost<HList<B, HList<A, EmptyHList>>>();
        var first = source.Create(HList.From(new A {Value=1}));
        var second = source.Create(HList.From(new A {Value=2}));
        int unrelated=0;
        world.Dispatcher.Listen((Entity _, in WorldEvents.Add<C> _) => { unrelated++; return false; });
        source.AddComponentToAll(new B[] {new(){Value=11},new(){Value=22}});
        Assert.Equal(11,first.Get<B>().Value);
        Assert.Equal(22,second.Get<B>().Value);
        target.RemoveHeadComponentFromAll();
        Assert.Equal(0,unrelated);
        Assert.Equal(1,first.Get<A>().Value);
        Assert.Equal(2,second.Get<A>().Value);
        Assert.Same(source,first.Host);
        Assert.Same(source,second.Host);
    }

    [Fact]
    public void Add_listener_can_register_set_listener_before_first_set()
    {
        using var world = new World();
        var source = world.GetArrayHost<HList<A, EmptyHList>>();
        _ = world.GetArrayHost<HList<B, HList<A, EmptyHList>>>();
        var first=source.Create(HList.From(new A {Value=1}));
        var second=source.Create(HList.From(new A {Value=2}));
        var trace=new List<string>();
        world.Dispatcher.Listen((Entity e, in WorldEvents.Add<B> _) => {
            trace.Add($"add:{e.Get<B>().Value}");
            if(trace.Count==1) world.Dispatcher.Listen((Entity item, in WorldEvents.Set<B> _) => { trace.Add($"set:{item.Get<B>().Value}"); return false; });
            return false;
        });
        source.AddComponentToAll(new B[] {new(){Value=11},new(){Value=22}});
        Assert.Equal(new[]{"add:11","set:11","add:22","set:22"},trace);
        Assert.Equal(11,first.Get<B>().Value);
        Assert.Equal(22,second.Get<B>().Value);
    }

    [Fact]
    public void Set_listener_observes_each_individual_move()
    {
        using var world = new World();
        var source = world.GetArrayHost<HList<A, EmptyHList>>();
        var target = world.GetArrayHost<HList<B, HList<A, EmptyHList>>>();
        source.Create(HList.From(new A {Value=1}));
        source.Create(HList.From(new A {Value=2}));
        var counts=new List<(int Source,int Target)>();
        world.Dispatcher.Listen((Entity _, in WorldEvents.Set<B> _) => { counts.Add((source.Count,target.Count)); return false; });
        source.AddComponentToAll(new B[] {new(){Value=11},new(){Value=22}});
        Assert.Equal(new[]{(1,1),(0,2)},counts);
    }

    [Fact]
    public void Batch_in_unrelated_typed_callback_does_not_flush_after_send_early()
    {
        using var world = new World();
        var source = world.GetArrayHost<HList<A, EmptyHList>>();
        var target = world.GetArrayHost<HList<B, HList<A, EmptyHList>>>();
        var first=source.Create(HList.From(new A {Value=1}));
        source.Create(HList.From(new A {Value=2}));
        var trace=new List<string>();
        world.Dispatcher.Listen((Entity _, in WorldEvents.Add<C> _) => {
            trace.Add("start");
            world.Dispatcher.RunAfterSend(() => trace.Add("after"));
            source.AddComponentToAll(new B[] {new(){Value=11},new(){Value=22}});
            target.RemoveHeadComponentFromAll();
            trace.Add("end");
            return false;
        });
        world.Dispatcher.Send(first,WorldEvents.Add<C>.Instance);
        Assert.Equal(new[]{"start","end","after"},trace);
        Assert.Same(source,first.Host);
    }

    [Fact]
    public void Clear_can_leave_typed_listeners_in_backing_storage()
    {
        using var world = new World();
        var source = world.GetArrayHost<HList<A, EmptyHList>>();
        _ = world.GetArrayHost<HList<B, HList<A, EmptyHList>>>();
        source.Create(HList.From(new A {Value=1}));
        source.Create(HList.From(new A {Value=2}));
        int calls=0;
        world.Dispatcher.Listen((Entity _, in WorldEvents.Add<B> _) => { calls++; return false; });
        world.Dispatcher.Clear();
        source.AddComponentToAll(new B[] {new(){Value=11},new(){Value=22}});
        Assert.Equal(2,calls);
    }

    [Fact]
    public void Large_typed_listener_buffer_preserves_observed_per_entity_order()
    {
        Type[] types=[typeof(byte),typeof(sbyte),typeof(short),typeof(ushort),typeof(int),typeof(uint),typeof(long),typeof(ulong),typeof(float),typeof(double),typeof(decimal),typeof(char),typeof(bool),typeof(string),typeof(object),typeof(Guid),typeof(DateTime)];
        foreach(var a in types) foreach(var b in types) {
            var eventType=typeof(SyntheticEvent<,>).MakeGenericType(a,b);
            _=typeof(EventTypeIndexer<>).MakeGenericType(eventType).GetProperty("Index")!.GetValue(null);
        }
        using var world = new World();
        var source=world.GetArrayHost<HList<A,EmptyHList>>();
        var target=world.GetArrayHost<HList<B,HList<A,EmptyHList>>>();
        source.Create(HList.From(new A {Value=1}));
        source.Create(HList.From(new A {Value=2}));
        int unrelated=0;
        world.Dispatcher.Listen((Entity _, in WorldEvents.Add<D> _) => {unrelated++;return false;});
        Assert.True(EventTypeIndexer<WorldEvents.Add<D>>.Index>=256);
        var counts=new List<(int Source,int Target)>();
        world.Dispatcher.Listen((Entity _, in WorldEvents.Set<B> _) => {counts.Add((source.Count,target.Count));return false;});
        source.AddComponentToAll(new B[] {new(){Value=11},new(){Value=22}});
        Assert.Equal(new[]{(1,1),(0,2)},counts);
        Assert.Equal(0,unrelated);
    }
}
