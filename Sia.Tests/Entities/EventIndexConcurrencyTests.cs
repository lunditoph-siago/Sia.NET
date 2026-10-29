namespace Sia.Tests.Entities;

public sealed class EventIndexConcurrencyTests
{
    public sealed class RaceOne : IEvent;
    public sealed class RaceTwo : IEvent;
    public sealed class Synthetic<T, U> : IEvent;
    public sealed class Marker : IEvent;
    public struct A { public int Value; }
    public struct B { public int Value; }
    public struct Unrelated { public int Value; }
    public struct OrderedA { public int Value; }
    public struct OrderedB { public int Value; }
    public struct ParallelA { public int Value; }
    public struct ParallelB { public int Value; }
    public struct ParallelUnrelated { public int Value; }

    [Fact]
    public void Parallel_first_use_keeps_each_event_index_unique_and_stable()
    {
        var values = new int[4096];
        Parallel.For(0, values.Length, i =>
            values[i] = (i & 1) == 0 ? EventTypeIndexer<RaceOne>.Index : EventTypeIndexer<RaceTwo>.Index);

        var first = EventTypeIndexer<RaceOne>.Index;
        var second = EventTypeIndexer<RaceTwo>.Index;
        Assert.NotEqual(first, second);
        for (int i = 0; i < values.Length; i++) {
            Assert.Equal((i & 1) == 0 ? first : second, values[i]);
        }
    }

    [Fact]
    public void Preflight_does_not_allocate_set_event_before_add_listener()
    {
        using var world = new World();
        var source = world.GetArrayHost<HList<OrderedA, EmptyHList>>();
        _ = world.GetArrayHost<HList<OrderedB, HList<OrderedA, EmptyHList>>>();
        source.Create(HList.From(new OrderedA { Value = 1 }));
        var markerIndex = -1;
        world.Dispatcher.Listen((Entity _, in WorldEvents.Add<OrderedB> _) => {
            markerIndex = EventTypeIndexer<Marker>.Index;
            return false;
        });

        source.AddComponentToAll(new OrderedB[] { new() { Value = 2 } });

        Assert.True(markerIndex >= 0);
        Assert.True(markerIndex < EventTypeIndexer<WorldEvents.Set<OrderedB>>.Index);
    }

    [Fact]
    public void Large_index_unrelated_listener_preserves_batch_values()
    {
        Type[] types = [
            typeof(byte), typeof(sbyte), typeof(short), typeof(ushort), typeof(int), typeof(uint),
            typeof(long), typeof(ulong), typeof(float), typeof(double), typeof(decimal), typeof(char),
            typeof(bool), typeof(string), typeof(object), typeof(Guid), typeof(DateTime)
        ];
        foreach (var a in types) {
            foreach (var b in types) {
                var eventType = typeof(Synthetic<,>).MakeGenericType(a, b);
                _ = typeof(EventTypeIndexer<>).MakeGenericType(eventType).GetProperty("Index")!.GetValue(null);
            }
        }

        using var world = new World();
        var source = world.GetArrayHost<HList<A, EmptyHList>>();
        var target = world.GetArrayHost<HList<B, HList<A, EmptyHList>>>();
        var first = source.Create(HList.From(new A { Value = 1 }));
        var second = source.Create(HList.From(new A { Value = 2 }));
        int calls = 0;
        world.Dispatcher.Listen((Entity _, in WorldEvents.Add<Unrelated> _) => { calls++; return false; });
        Assert.True(EventTypeIndexer<WorldEvents.Add<Unrelated>>.Index >= 256);

        source.AddComponentToAll(new B[] { new() { Value = 11 }, new() { Value = 22 } });
        Assert.Equal(11, first.Get<B>().Value);
        Assert.Equal(22, second.Get<B>().Value);
        target.RemoveHeadComponentFromAll();

        Assert.Equal(0, calls);
        Assert.Same(source, first.Host);
        Assert.Same(source, second.Host);
        Assert.Equal(1, first.Get<A>().Value);
        Assert.Equal(2, second.Get<A>().Value);
    }

    [Fact]
    public void Concurrent_worlds_preserve_relevant_and_unrelated_typed_listeners()
    {
        Parallel.For(0, 32, i => {
            using var world = new World();
            var source = world.GetArrayHost<HList<ParallelA, EmptyHList>>();
            var target = world.GetArrayHost<HList<ParallelB, HList<ParallelA, EmptyHList>>>();
            var first = source.Create(HList.From(new ParallelA { Value = i }));
            var second = source.Create(HList.From(new ParallelA { Value = i + 1 }));
            int calls = 0;
            if ((i & 1) == 0) {
                world.Dispatcher.Listen((Entity _, in WorldEvents.Add<ParallelB> _) => { calls++; return false; });
            }
            else {
                world.Dispatcher.Listen((Entity _, in WorldEvents.Add<ParallelUnrelated> _) => { calls++; return false; });
            }

            source.AddComponentToAll(new ParallelB[] { new() { Value = i + 10 }, new() { Value = i + 20 } });
            Assert.Equal((i & 1) == 0 ? 2 : 0, calls);
            Assert.Equal(i + 10, first.Get<ParallelB>().Value);
            Assert.Equal(i + 20, second.Get<ParallelB>().Value);
            target.RemoveHeadComponentFromAll();
            Assert.Same(source, first.Host);
            Assert.Same(source, second.Host);
        });
    }
}
