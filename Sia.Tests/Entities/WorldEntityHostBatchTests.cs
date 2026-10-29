namespace Sia.Tests.Entities;

public sealed class WorldEntityHostBatchTests
{
    public struct A { public int Value; public string? Text; }
    public struct B { public int Value; }
    private sealed class Sink : IEventSink<Entity, WorldEvents.Add<B>>
    {
        public int Calls;
        public void OnEvent(Entity entity, in WorldEvents.Add<B> e) => Calls++;
    }
    private sealed class Listener : IEventListener
    {
        public int Calls;
        public bool OnEvent<T>(Entity entity, in T e) where T : IEvent { Calls++; return false; }
    }
    private sealed class TrackingHost<T> : ArrayEntityHost<T> where T : struct, IHList
    {
        public int Adds;
        public override void Add<TComponent>(Entity entity, in TComponent initial)
        {
            Adds++;
            base.Add(entity, initial);
        }
    }

    public struct CustomEmpty : IHList
    {
        public readonly void Handle<THandler>(in THandler handler) where THandler : IHListHandler { }
        public readonly void HandleHead<THandler>(in THandler handler) where THandler : IGenericHandler { }
        public readonly void HandleHeadRef<THandler>(ref THandler handler) where THandler : IRefGenericHandler { }
        public readonly void HandleTail<THandler>(in THandler handler) where THandler : IGenericHandler<IHList> { }
        public readonly void HandleTailRef<THandler>(ref THandler handler) where THandler : IRefGenericHandler<IHList> { }
        public readonly void Filter<TPredicate, THandler>(in TPredicate predicate, in THandler handler)
            where TPredicate : IGenericPredicate where THandler : IGenericStructHandler<IHList> => handler.Handle(this);
        public readonly void Concat<THList, THandler>(in THList list, in THandler handler)
            where THList : struct, IHList where THandler : IGenericStructHandler<IHList> => handler.Handle(list);
        public readonly bool Remove<TValue, THandler>(TypeProxy<TValue> proxy, in THandler handler)
            where THandler : IGenericStructHandler<IHList> => false;
        public readonly bool Remove<TValue, THandler>(in TValue value, in THandler handler)
            where TValue : IEquatable<TValue> where THandler : IGenericStructHandler<IHList> => false;
        public static void HandleTypes<THandler>(in THandler handler) where THandler : IGenericTypeHandler { }
    }

    [Fact]
    public void Whole_host_roundtrip_preserves_values_identity_order_and_versions()
    {
        using var world = new World();
        var source = world.GetArrayHost<HList<A, EmptyHList>>();
        var target = world.GetArrayHost<HList<B, HList<A, EmptyHList>>>();
        var entities = new Entity[5];
        var initial = new B[5];
        for (var i = 0; i < entities.Length; i++) {
            entities[i] = source.Create(HList.From(new A { Value = i, Text = $"a{i}" }));
            initial[i] = new B { Value = i + 10 };
        }
        var sourceVersion = source.Version;
        var targetVersion = target.Version;
        source.AddComponentToAll(initial);
        Assert.Equal(0, source.Count);
        Assert.Equal(5, target.Count);
        Assert.Equal(sourceVersion + 5, source.Version);
        Assert.Equal(targetVersion + 5, target.Version);
        for (var i = 0; i < entities.Length; i++) {
            Assert.Same(target, entities[i].Host);
            Assert.Equal(i, entities[i].Slot);
            Assert.Equal(i + 10, entities[i].Get<B>().Value);
            Assert.Equal($"a{i}", entities[i].Get<A>().Text);
            Assert.Null(source.InnerHost.Buffer.AsSpan()[i].Head.Text);
        }
        target.RemoveHeadComponentFromAll();
        Assert.Equal(5, source.Count);
        Assert.Equal(0, target.Count);
        Assert.Equal(sourceVersion + 10, source.Version);
        Assert.Equal(targetVersion + 10, target.Version);
        for (var i = 0; i < entities.Length; i++) {
            Assert.Same(source, entities[i].Host);
            Assert.Equal(entities[i], source.GetEntity(i));
            Assert.Equal(i, entities[i].Slot);
            Assert.False(entities[i].Contains<B>());
            Assert.Equal(i, entities[i].Get<A>().Value);
            Assert.Equal($"a{i}", entities[i].Get<A>().Text);
            Assert.Null(target.InnerHost.Buffer.AsSpan()[i].Tail.Head.Text);
        }
        Assert.Equal(5, world.Count);
    }

    [Theory]
    [InlineData("global")]
    [InlineData("typed")]
    [InlineData("target")]
    [InlineData("sink")]
    [InlineData("source")]
    [InlineData("target-host")]
    public void Observers_receive_expected_notifications_in_the_fallback(string reason)
    {
        using var world = new World();
        var source = world.GetArrayHost<HList<A, EmptyHList>>();
        var target = world.GetArrayHost<HList<B, HList<A, EmptyHList>>>();
        var first = source.Create(HList.From(new A { Value = 1, Text = "first" }));
        var second = source.Create(HList.From(new A { Value = 2, Text = "second" }));
        var listener = new Listener();
        var sink = new Sink();
        var typedCalls = 0;
        var moved = 0;
        if (reason == "global") world.Dispatcher.Listen(listener);
        if (reason == "typed") world.Dispatcher.Listen((Entity _, in WorldEvents.Add<B> _) => { typedCalls++; return false; });
        if (reason == "target") world.Dispatcher.Listen(first, listener);
        if (reason == "sink") world.Dispatcher.AttachEventSink(first, sink);
        if (reason == "source") source.OnEntityMovedOut += (_, _) => moved++;
        if (reason == "target-host") target.OnEntityMovedIn += (_, _) => moved++;
        B[] values = [new B { Value = 11 }, new B { Value = 22 }];
        var sourceVersion = source.Version;
        var targetVersion = target.Version;
        source.AddComponentToAll(values);
        Assert.Equal(0, source.Count);
        Assert.Equal(2, target.Count);
        Assert.Equal(sourceVersion + 2, source.Version);
        Assert.Equal(targetVersion + 2, target.Version);
        Assert.Same(target, first.Host);
        Assert.Same(target, second.Host);
        Assert.Equal("first", first.Get<A>().Text);
        Assert.Equal("second", second.Get<A>().Text);
        Assert.Equal(11, first.Get<B>().Value);
        Assert.Equal(22, second.Get<B>().Value);
        if (reason == "global") Assert.Equal(4, listener.Calls);
        if (reason == "target") Assert.Equal(2, listener.Calls);
        if (reason == "typed") Assert.Equal(2, typedCalls);
        if (reason == "sink") Assert.Equal(1, sink.Calls);
        if (reason is "source" or "target-host") Assert.Equal(2, moved);
    }

    [Fact]
    public void Observed_fallback_preserves_per_entity_migration_and_event_order()
    {
        using var world = new World();
        var source = world.GetArrayHost<HList<A, EmptyHList>>();
        var target = world.GetArrayHost<HList<B, HList<A, EmptyHList>>>();
        var first = source.Create(HList.From(new A { Value = 1 }));
        var second = source.Create(HList.From(new A { Value = 2 }));
        var trace = new List<string>();
        source.OnEntityMovedOut += (e, _) => trace.Add($"out:{e.Get<A>().Value}");
        target.OnEntityMovedIn += (e, _) => trace.Add($"in:{e.Get<A>().Value}");
        world.Dispatcher.Listen((Entity e, in WorldEvents.Add<B> _) => { trace.Add($"add:{e.Get<B>().Value}"); return false; });
        world.Dispatcher.Listen((Entity e, in WorldEvents.Set<B> _) => { trace.Add($"set:{e.Get<B>().Value}"); return false; });
        source.AddComponentToAll(new B[] { new B { Value = 11 }, new B { Value = 22 } });
        Assert.Equal(new[] { "out:1", "in:1", "add:11", "set:11", "out:2", "in:2", "add:22", "set:22" }, trace);
        Assert.Equal(11, first.Get<B>().Value);
        Assert.Equal(22, second.Get<B>().Value);

        trace.Clear();
        target.OnEntityMovedOut += (e, _) => trace.Add($"out:{e.Get<A>().Value}");
        source.OnEntityMovedIn += (e, _) => trace.Add($"in:{e.Get<A>().Value}");
        world.Dispatcher.Listen((Entity e, in WorldEvents.Remove<B> _) => { trace.Add($"remove:{e.Get<A>().Value}"); return false; });
        target.RemoveHeadComponentFromAll();
        Assert.Equal(new[] { "out:1", "in:1", "remove:1", "out:2", "in:2", "remove:2" }, trace);
        Assert.Same(source, first.Host);
        Assert.Same(source, second.Host);
    }

    [Fact]
    public void Throwing_listener_stops_fallback_after_current_entity()
    {
        using var world = new World();
        var source = world.GetArrayHost<HList<A, EmptyHList>>();
        _ = world.GetArrayHost<HList<B, HList<A, EmptyHList>>>();
        var first = source.Create(HList.From(new A { Value = 1 }));
        var second = source.Create(HList.From(new A { Value = 2 }));
        world.Dispatcher.Listen((Entity _, in WorldEvents.Add<B> _) => throw new InvalidOperationException("observer"));
        Assert.Throws<InvalidOperationException>(() => source.AddComponentToAll(new B[] { new B { Value = 11 }, new B { Value = 22 } }));
        Assert.Equal(11, first.Get<B>().Value);
        Assert.False(second.Contains<B>());
        Assert.Same(source, second.Host);
    }

    [Fact]
    public void Values_length_mismatch_and_duplicate_component_do_not_move_entities()
    {
        using var world = new World();
        var source = world.GetArrayHost<HList<A, EmptyHList>>();
        var entity = source.Create(HList.From(new A { Value = 5 }));
        Assert.Throws<ArgumentException>(() => source.AddComponentToAll(Array.Empty<B>()));
        Assert.Throws<ComponentConflictException>(() => source.AddComponentToAll(new A[] { new A { Value = 9 } }));
        Assert.Same(source, entity.Host);
        Assert.Equal(5, entity.Get<A>().Value);
    }

    [Fact]
    public void Fallback_creates_missing_target_before_first_component_event()
    {
        using var world = new World();
        var source = world.GetArrayHost<HList<A, EmptyHList>>();
        var entity = source.Create(HList.From(new A { Value = 1 }));
        var trace = new List<string>();
        world.OnEntityHostAdded += _ => trace.Add("host");
        world.Dispatcher.Listen((Entity _, in WorldEvents.Add<B> _) => { trace.Add("add"); return false; });
        source.AddComponentToAll(new B[] { new B { Value = 11 } });
        Assert.Equal(new[] { "host", "add" }, trace);
        Assert.Equal(11, entity.Get<B>().Value);
    }

    [Fact]
    public void Derived_inner_host_uses_its_virtual_add_override()
    {
        using var world = new World();
        var inner = new TrackingHost<HList<A, EmptyHList>>();
        var source = new WorldEntityHost<HList<A, EmptyHList>, ArrayEntityHost<HList<A, EmptyHList>>>(world, inner);
        world.UnsafeAddRawHost(source);
        _ = world.GetArrayHost<HList<B, HList<A, EmptyHList>>>();
        var first = source.Create(HList.From(new A { Value = 1 }));
        var second = source.Create(HList.From(new A { Value = 2 }));
        source.AddComponentToAll(new B[] { new B { Value = 11 }, new B { Value = 22 } });
        Assert.Equal(2, inner.Adds);
        Assert.Equal(11, first.Get<B>().Value);
        Assert.Equal(22, second.Get<B>().Value);
    }

    [Fact]
    public void Custom_hlist_tail_uses_the_ordinary_path()
    {
        using var world = new World();
        var source = world.GetArrayHost<HList<A, CustomEmpty>>();
        var target = world.GetArrayHost<HList<B, HList<A, CustomEmpty>>>();
        var entity = source.Create(HList.Cons(new A { Value = 1 }, new CustomEmpty()));
        B[] values = [new B { Value = 11 }];
        source.AddComponentToAll(values);
        Assert.Equal(1, entity.Get<A>().Value);
        Assert.Equal(11, entity.Get<B>().Value);
    }

    [Fact]
    public void Callback_cannot_change_snapshotted_values_for_later_entities()
    {
        using var world = new World();
        var source = world.GetArrayHost<HList<A, EmptyHList>>();
        _ = world.GetArrayHost<HList<B, HList<A, EmptyHList>>>();
        var first = source.Create(HList.From(new A { Value = 1 }));
        var second = source.Create(HList.From(new A { Value = 2 }));
        B[] values = [new B { Value = 11 }, new B { Value = 22 }];
        world.Dispatcher.Listen((Entity e, in WorldEvents.Add<B> _) => { if (e == first) values[1].Value = 99; return false; });
        source.AddComponentToAll(values);
        Assert.Equal(99, values[1].Value);
        Assert.Equal(22, second.Get<B>().Value);
    }

    [Fact]
    public void Callback_destroying_later_entity_stops_at_stale_snapshot_entry()
    {
        using var world = new World();
        var source = world.GetArrayHost<HList<A, EmptyHList>>();
        _ = world.GetArrayHost<HList<B, HList<A, EmptyHList>>>();
        var first = source.Create(HList.From(new A { Value = 1 }));
        var second = source.Create(HList.From(new A { Value = 2 }));
        world.Dispatcher.Listen((Entity e, in WorldEvents.Add<B> _) => {
            if (e == first) world.Dispatcher.RunAfterSend(second.Destroy);
            return false;
        });
        Assert.Throws<ObjectDisposedException>(() => source.AddComponentToAll(new B[] { new B { Value = 11 }, new B { Value = 22 } }));
        Assert.Equal(11, first.Get<B>().Value);
        Assert.False(second.IsValid);
        Assert.Equal(1, world.Count);
    }

    [Fact]
    public void Batch_appends_to_a_nonempty_target_and_can_move_all_back()
    {
        using var world = new World();
        var source = world.GetArrayHost<HList<A, EmptyHList>>();
        var target = world.GetArrayHost<HList<B, HList<A, EmptyHList>>>();
        var existing = target.Create(HList.From(new B { Value = 7 }, new A { Value = 70 }));
        var first = source.Create(HList.From(new A { Value = 1 }));
        var second = source.Create(HList.From(new A { Value = 2 }));
        source.AddComponentToAll(new B[] { new B { Value = 11 }, new B { Value = 22 } });
        Assert.Equal(3, target.Count);
        Assert.Equal(existing, target.GetEntity(0));
        Assert.Equal(first, target.GetEntity(1));
        Assert.Equal(second, target.GetEntity(2));
        Assert.Equal(7, existing.Get<B>().Value);
        target.RemoveHeadComponentFromAll();
        Assert.Equal(3, source.Count);
        Assert.Equal(existing, source.GetEntity(0));
        Assert.Equal(first, source.GetEntity(1));
        Assert.Equal(second, source.GetEntity(2));
        Assert.Equal(70, existing.Get<A>().Value);
        Assert.Equal(3, world.Count);
    }

    [Fact]
    public void Input_values_may_alias_the_source_entity_span()
    {
        using var world = new World();
        var source = world.GetArrayHost<HList<A, EmptyHList>>();
        _ = world.GetArrayHost<HList<Entity, HList<A, EmptyHList>>>();
        var first = source.Create(HList.From(new A { Value = 1 }));
        var second = source.Create(HList.From(new A { Value = 2 }));
        source.AddComponentToAll(source.UnsafeGetEntitySpan());
        Assert.Equal(first, first.Get<Entity>());
        Assert.Equal(second, second.Get<Entity>());
        Assert.Equal(1, first.Get<A>().Value);
        Assert.Equal(2, second.Get<A>().Value);
    }

    [Fact]
    public void Empty_hlist_entities_can_make_a_batch_roundtrip()
    {
        using var world = new World();
        var source = world.GetArrayHost<EmptyHList>();
        var target = world.GetArrayHost<HList<B, EmptyHList>>();
        var first = source.Create();
        var second = source.Create();
        source.AddComponentToAll(new B[] { new B { Value = 11 }, new B { Value = 22 } });
        Assert.Same(target, first.Host);
        Assert.Equal(11, first.Get<B>().Value);
        Assert.Equal(22, second.Get<B>().Value);
        target.RemoveHeadComponentFromAll();
        Assert.Same(source, first.Host);
        Assert.Same(source, second.Host);
        Assert.False(first.Contains<B>());
        Assert.False(second.Contains<B>());
    }

    [Fact]
    public void Existing_queries_follow_batch_host_membership()
    {
        using var world = new World();
        var source = world.GetArrayHost<HList<A, EmptyHList>>();
        var target = world.GetArrayHost<HList<B, HList<A, EmptyHList>>>();
        var first = source.Create(HList.From(new A { Value = 1 }));
        var second = source.Create(HList.From(new A { Value = 2 }));
        var query = world.Query(Matchers.Of<B>());
        Assert.Empty(query.Hosts.SelectMany(static h => h));
        source.AddComponentToAll(new B[] { new B { Value = 11 }, new B { Value = 22 } });
        Assert.Equal(new[] { first, second }, query.Hosts.SelectMany(static h => h));
        target.RemoveHeadComponentFromAll();
        Assert.Empty(query.Hosts.SelectMany(static h => h));
    }

    [Fact]
    public void Unmanaged_host_uses_the_same_snapshot_and_event_semantics()
    {
        using var world = new World();
        var source = world.GetUnmanagedArrayHost<HList<int, EmptyHList>>();
        var target = world.GetUnmanagedArrayHost<HList<B, HList<int, EmptyHList>>>();
        var first = source.Create(HList.From(1));
        var second = source.Create(HList.From(2));
        var events = 0;
        world.Dispatcher.Listen((Entity _, in WorldEvents.Add<B> _) => { events++; return false; });
        source.AddComponentToAll(new B[] { new B { Value = 11 }, new B { Value = 22 } });
        Assert.Equal(2, events);
        Assert.Same(target, first.Host);
        Assert.Equal(11, first.Get<B>().Value);
        Assert.Equal(22, second.Get<B>().Value);
        target.RemoveHeadComponentFromAll();
        Assert.Same(source, first.Host);
        Assert.Same(source, second.Host);
        Assert.Equal(1, first.Get<int>());
        Assert.Equal(2, second.Get<int>());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(33)]
    [InlineData(512)]
    public void Batch_matches_individual_operations_after_source_reordering(int count)
    {
        using var expectedWorld = new World();
        using var actualWorld = new World();
        var expectedSource = expectedWorld.GetArrayHost<HList<A, EmptyHList>>();
        var actualSource = actualWorld.GetArrayHost<HList<A, EmptyHList>>();
        var expectedTarget = expectedWorld.GetArrayHost<HList<B, HList<A, EmptyHList>>>();
        var actualTarget = actualWorld.GetArrayHost<HList<B, HList<A, EmptyHList>>>();
        var expected = new Entity[count];
        var actual = new Entity[count];
        for (var i = 0; i < count; i++) {
            var a = new A { Value = i, Text = $"v{i}" };
            expected[i] = expectedSource.Create(HList.From(a));
            actual[i] = actualSource.Create(HList.From(a));
        }
        for (var i = 1; i < count; i += 4) { expected[i].Destroy(); actual[i].Destroy(); }
        var expectedOrder = expectedSource.UnsafeGetEntitySpan().ToArray();
        var actualOrder = actualSource.UnsafeGetEntitySpan().ToArray();
        var values = new B[expectedOrder.Length];
        for (var i = 0; i < values.Length; i++) {
            Assert.Equal(expectedOrder[i].Get<A>().Value, actualOrder[i].Get<A>().Value);
            values[i] = new B { Value = 1000 + i };
            expectedOrder[i].Add(values[i]);
        }
        actualSource.AddComponentToAll(values);
        Assert.Equal(expectedWorld.Count, actualWorld.Count);
        Assert.Equal(expectedSource.Version, actualSource.Version);
        Assert.Equal(expectedTarget.Version, actualTarget.Version);
        Assert.Equal(expectedTarget.Count, actualTarget.Count);
        for (var i = 0; i < actualTarget.Count; i++) {
            Assert.Equal(expectedTarget.GetEntity(i).Get<A>().Value, actualTarget.GetEntity(i).Get<A>().Value);
            Assert.Equal(expectedTarget.GetEntity(i).Get<A>().Text, actualTarget.GetEntity(i).Get<A>().Text);
            Assert.Equal(expectedTarget.GetEntity(i).Get<B>().Value, actualTarget.GetEntity(i).Get<B>().Value);
            Assert.Equal(i, actualTarget.GetEntity(i).Slot);
        }
        foreach (var e in expectedTarget.UnsafeGetEntitySpan().ToArray()) e.Remove<B>();
        actualTarget.RemoveHeadComponentFromAll();
        Assert.Equal(expectedSource.Count, actualSource.Count);
        Assert.Equal(expectedSource.Version, actualSource.Version);
        Assert.Equal(expectedTarget.Version, actualTarget.Version);
        for (var i = 0; i < actualSource.Count; i++) {
            Assert.Equal(expectedSource.GetEntity(i).Get<A>().Value, actualSource.GetEntity(i).Get<A>().Value);
            Assert.Equal(expectedSource.GetEntity(i).Get<A>().Text, actualSource.GetEntity(i).Get<A>().Text);
            Assert.Equal(i, actualSource.GetEntity(i).Slot);
        }
    }

    [Fact]
    public void Actor_owner_check_runs_before_batch_mutation()
    {
        using var world = new World();
        var source = world.GetArrayHost<HList<A, EmptyHList>>();
        _ = world.GetArrayHost<HList<B, HList<A, EmptyHList>>>();
        var entity = source.Create(HList.From(new A { Value = 5 }));
        using var actor = new WorldActor(world);
        try {
            Assert.Throws<InvalidOperationException>(() => source.AddComponentToAll(new B[] { new B { Value = 11 } }));
            Assert.Same(source, entity.Host);
            Assert.Equal(5, entity.Get<A>().Value);
        }
        finally {
            actor.Complete();
            actor.Run();
        }
    }

    [Fact]
    public void Batch_during_an_active_slice_matches_individual_migration()
    {
        static (int[] visits, int count, Type? error, string? message) Run(bool batch)
        {
            using var world = new World();
            var source = world.GetArrayHost<HList<int, EmptyHList>>();
            _ = world.GetArrayHost<HList<B, HList<int, EmptyHList>>>();
            for (var i = 0; i < 4; i++) source.Create(HList.From(i + 1));
            var snapshot = source.UnsafeGetEntitySpan().ToArray();
            var query = world.Query(Matchers.Of<int>());
            var visits = new List<int>();
            var moved = false;
            Exception? error = null;
            try {
                query.ForSlice((ref int value) => {
                    visits.Add(value);
                    if (moved) return;
                    moved = true;
                    if (batch) source.AddComponentToAll(new B[] { new(), new(), new(), new() });
                    else foreach (var e in snapshot) e.Add(new B());
                });
            }
            catch (Exception caught) { error = caught; }
            return ([.. visits], world.Count, error?.GetType(), error?.Message);
        }
        var expected = Run(false);
        var actual = Run(true);
        Assert.Equal(expected.count, actual.count);
        Assert.Equal(expected.visits, actual.visits);
        Assert.Equal(expected.error, actual.error);
        Assert.Equal(expected.message, actual.message);
    }
}
