namespace Sia.Tests.Entities;

public sealed class WorldSiblingTransitionTests
{
    public struct A { public int Value; }
    public struct B { public int Value; }
    public struct C { public string? Text; }

    [Fact]
    public void AddMany_and_RemoveMany_keep_values_and_component_event_order()
    {
        using var world = new World();
        var entity = world.Create(HList.From(new A { Value = 5 }));
        var trace = new List<string>();
        world.Dispatcher.Listen((Entity _, in WorldEvents.Add<B> _) => { trace.Add("add-b"); return false; });
        world.Dispatcher.Listen((Entity _, in WorldEvents.Set<B> _) => { trace.Add("set-b"); return false; });
        world.Dispatcher.Listen((Entity _, in WorldEvents.Add<C> _) => { trace.Add("add-c"); return false; });
        world.Dispatcher.Listen((Entity _, in WorldEvents.Set<C> _) => { trace.Add("set-c"); return false; });
        world.Dispatcher.Listen((Entity _, in WorldEvents.Remove<B> _) => { trace.Add("remove-b"); return false; });
        world.Dispatcher.Listen((Entity _, in WorldEvents.Remove<C> _) => { trace.Add("remove-c"); return false; });
        entity.AddMany(HList.From(new B { Value = 7 }, new C { Text = "kept" }));
        Assert.Equal(5, entity.Get<A>().Value);
        Assert.Equal(7, entity.Get<B>().Value);
        Assert.Equal("kept", entity.Get<C>().Text);
        entity.RemoveMany<HList<B, HList<C, EmptyHList>>>();
        Assert.True(entity.IsValid);
        Assert.Equal(5, entity.Get<A>().Value);
        Assert.False(entity.Contains<B>());
        Assert.False(entity.Contains<C>());
        Assert.Equal(new[] { "add-b", "set-b", "add-c", "set-c", "remove-b", "remove-c" }, trace);
    }

    [Fact]
    public void Adding_duplicate_or_using_recycled_stale_handle_preserves_current_entity()
    {
        using var world = new World();
        var host = world.GetArrayHost<HList<A, EmptyHList>>();
        var stale = host.Create(HList.From(new A { Value = 1 }));
        stale.Destroy();
        var current = host.Create(HList.From(new A { Value = 2 }));
        Assert.Throws<ComponentConflictException>(() => current.Add(new A { Value = 3 }));
        Assert.Throws<ObjectDisposedException>(() => host.Add(stale, new B { Value = 4 }));
        Assert.True(current.IsValid);
        Assert.Same(host, current.Host);
        Assert.Equal(2, current.Get<A>().Value);
        Assert.False(current.Contains<B>());
    }
    public sealed class AlternateHost<T> : ArrayEntityHost<T> where T : struct, IHList;
    public sealed class SwitchingHost<T> : ArrayEntityHost<T> where T : struct, IHList
    {
        public int Calls;
        public override void GetSiblingHostType<UEntity, THandler>(in THandler handler)
        {
            Calls++;
            if ((Calls & 1) == 1) handler.Handle<ArrayEntityHost<UEntity>>();
            else handler.Handle<AlternateHost<UEntity>>();
        }
    }
    public sealed class OverrideHost<T> : ArrayEntityHost<T> where T : struct, IHList
    {
        public int Adds;
        public int Removes;
        public override void Add<TComponent>(Entity entity, in TComponent initial) { Adds++; base.Add(entity, initial); }
        public override void Remove<TComponent>(Entity entity, out bool success) { Removes++; base.Remove<TComponent>(entity, out success); }
    }

    [Fact]
    public void Sibling_creation_callback_can_change_source_data_before_move()
    {
        using var world = new World();
        var source = world.GetArrayHost<HList<A, EmptyHList>>();
        var entity = source.Create(HList.From(new A { Value = 5 }));
        world.OnEntityHostAdded += _ => entity.Get<A>().Value = 19;
        entity.Add(new B { Value = 7 });
        Assert.Equal(19, entity.Get<A>().Value);
        Assert.Equal(7, entity.Get<B>().Value);
    }

    [Fact]
    public void Derived_inner_host_keeps_add_and_remove_virtual_dispatch()
    {
        using (var world = new World()) {
            var inner = new OverrideHost<HList<A, EmptyHList>>();
            var wrapper = new WorldEntityHost<HList<A, EmptyHList>, ArrayEntityHost<HList<A, EmptyHList>>>(world, inner);
            world.UnsafeAddRawHost(wrapper);
            var entity = wrapper.Create(HList.From(new A { Value = 5 }));
            entity.Add(new B { Value = 7 });
            Assert.Equal(1, inner.Adds);
            Assert.Equal(7, entity.Get<B>().Value);
        }
        using (var world = new World()) {
            var inner = new OverrideHost<HList<B, HList<A, EmptyHList>>>();
            var wrapper = new WorldEntityHost<HList<B, HList<A, EmptyHList>>, ArrayEntityHost<HList<B, HList<A, EmptyHList>>>>(world, inner);
            world.UnsafeAddRawHost(wrapper);
            var entity = wrapper.Create(HList.Cons(new B { Value = 7 }, HList.From(new A { Value = 5 })));
            entity.Remove<B>();
            Assert.Equal(1, inner.Removes);
            Assert.Equal(5, entity.Get<A>().Value);
            Assert.False(entity.Contains<B>());
        }
    }

    [Fact]
    public void Host_added_callback_can_reenter_sibling_lookup()
    {
        using var world = new World();
        var source = world.GetArrayHost<HList<A, EmptyHList>>();
        IEntityHost<HList<B, HList<A, EmptyHList>>>? reentered = null;
        var added = 0;
        world.OnEntityHostAdded += _ => {
            added++;
            reentered = source.GetSiblingHost<HList<B, HList<A, EmptyHList>>>();
        };
        var result = source.GetSiblingHost<HList<B, HList<A, EmptyHList>>>();
        Assert.Same(result, reentered);
        Assert.Equal(1, added);
    }

    [Fact]
    public void Throwing_host_added_callback_leaves_registered_sibling_available()
    {
        using var world = new World();
        var source = world.GetArrayHost<HList<A, EmptyHList>>();
        var added = 0;
        world.OnEntityHostAdded += _ => { added++; throw new InvalidOperationException("observer"); };
        Assert.Throws<InvalidOperationException>(() => source.GetSiblingHost<HList<B, HList<A, EmptyHList>>>());
        Assert.True(world.TryGetHost<HList<B, HList<A, EmptyHList>>, ArrayEntityHost<HList<B, HList<A, EmptyHList>>>>(out var registered));
        Assert.Same(registered, source.GetSiblingHost<HList<B, HList<A, EmptyHList>>>());
        Assert.Equal(1, added);
    }

    [Fact]
    public void Derived_host_can_choose_a_different_sibling_type_each_time()
    {
        using var world = new World();
        var inner = new SwitchingHost<HList<A, EmptyHList>>();
        var host = new WorldEntityHost<HList<A, EmptyHList>, ArrayEntityHost<HList<A, EmptyHList>>>(world, inner);
        world.UnsafeAddRawHost(host);
        var first = host.GetSiblingHost<HList<B, HList<A, EmptyHList>>>();
        var second = host.GetSiblingHost<HList<B, HList<A, EmptyHList>>>();
        var third = host.GetSiblingHost<HList<B, HList<A, EmptyHList>>>();
        Assert.IsType<WorldEntityHost<HList<B, HList<A, EmptyHList>>, ArrayEntityHost<HList<B, HList<A, EmptyHList>>>>>(first);
        Assert.IsType<WorldEntityHost<HList<B, HList<A, EmptyHList>>, AlternateHost<HList<B, HList<A, EmptyHList>>>>>(second);
        Assert.Same(first, third);
        Assert.Equal(3, inner.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Released_sibling_is_recreated_and_host_notifications_stay_in_order(bool unmanaged)
    {
        using var world = new World();
        var trace = new List<string>();
        world.OnEntityHostAdded += _ => trace.Add("added");
        world.OnEntityHostRemoved += _ => trace.Add("removed");
        if (unmanaged) {
            var host = world.GetUnmanagedArrayHost<HList<A, EmptyHList>>();
            var first = host.GetSiblingHost<HList<B, HList<A, EmptyHList>>>();
            Assert.Same(first, host.GetSiblingHost<HList<B, HList<A, EmptyHList>>>());
            Assert.True(world.ReleaseHost<WorldEntityHost<HList<B, HList<A, EmptyHList>>, UnmanagedArrayEntityHost<HList<B, HList<A, EmptyHList>>>>>());
            Assert.NotSame(first, host.GetSiblingHost<HList<B, HList<A, EmptyHList>>>());
        }
        else {
            var host = world.GetArrayHost<HList<A, EmptyHList>>();
            var first = host.GetSiblingHost<HList<B, HList<A, EmptyHList>>>();
            Assert.Same(first, host.GetSiblingHost<HList<B, HList<A, EmptyHList>>>());
            Assert.True(world.ReleaseHost<WorldEntityHost<HList<B, HList<A, EmptyHList>>, ArrayEntityHost<HList<B, HList<A, EmptyHList>>>>>());
            Assert.NotSame(first, host.GetSiblingHost<HList<B, HList<A, EmptyHList>>>());
        }
        Assert.Equal(new[] { "added", "added", "removed", "added" }, trace);
    }

    [Fact]
    public void Add_and_remove_preserve_identity_data_and_event_order()
    {
        using var world = new World();
        var source = world.GetArrayHost<HList<A, EmptyHList>>();
        var dest = world.GetArrayHost<HList<B, HList<A, EmptyHList>>>();
        var entity = source.Create(HList.From(new A { Value = 42 }));
        var trace = new List<string>();
        source.OnEntityMovedOut += (_, _) => trace.Add("source-out");
        source.OnEntityMovedIn += (_, _) => trace.Add("source-in");
        dest.OnEntityMovedOut += (_, _) => trace.Add("dest-out");
        dest.OnEntityMovedIn += (_, _) => trace.Add("dest-in");
        world.Dispatcher.Listen((Entity _, in WorldEvents.Add<B> _) => { trace.Add("add"); return false; });
        world.Dispatcher.Listen((Entity _, in WorldEvents.Set<B> _) => { trace.Add("set"); return false; });
        world.Dispatcher.Listen((Entity _, in WorldEvents.Remove<B> _) => { trace.Add("remove"); return false; });
        entity.Add(new B { Value = 7 });
        Assert.Same(dest, entity.Host);
        Assert.Equal(42, entity.Get<A>().Value);
        Assert.Equal(7, entity.Get<B>().Value);
        entity.Remove<B>();
        Assert.True(entity.IsValid);
        Assert.Same(source, entity.Host);
        Assert.Equal(42, entity.Get<A>().Value);
        Assert.False(entity.Contains<B>());
        Assert.Equal(new[] { "source-out", "dest-in", "add", "set", "dest-out", "source-in", "remove" }, trace);
    }
}
