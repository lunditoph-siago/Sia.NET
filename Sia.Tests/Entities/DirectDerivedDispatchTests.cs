namespace Sia.Tests.Entities;

public sealed class DirectDerivedDispatchTests
{
    private record struct A(int Value);
    private record struct B(int Value);

    private sealed class TrackingHost<T> : ArrayEntityHost<T> where T : struct, IHList
    {
        public int AddCalls;
        public int RemoveCalls;
        public override void Add<TComponent>(Entity e, in TComponent value)
        {
            AddCalls++;
            base.Add(e, value);
        }
        public override void Remove<TComponent>(Entity e, out bool success)
        {
            RemoveCalls++;
            base.Remove<TComponent>(e, out success);
        }
    }

    [Fact]
    public void Builtin_declared_type_preserves_derived_add_dispatch()
    {
        using var world = new World();
        var inner = new TrackingHost<HList<A, EmptyHList>>();
        var host = world.UnsafeAddRawHost(
            new WorldEntityHost<HList<A, EmptyHList>, ArrayEntityHost<HList<A, EmptyHList>>>(world, inner));
        var entity = host.Create(HList.From(new A(3)));
        entity.Add(new B(4));
        Assert.Equal(1, inner.AddCalls);
        Assert.Equal(4, entity.Get<B>().Value);
    }

    [Fact]
    public void Builtin_declared_type_preserves_derived_remove_dispatch()
    {
        using var world = new World();
        var inner = new TrackingHost<HList<B, HList<A, EmptyHList>>>();
        var host = world.UnsafeAddRawHost(
            new WorldEntityHost<HList<B, HList<A, EmptyHList>>, ArrayEntityHost<HList<B, HList<A, EmptyHList>>>>(world, inner));
        var entity = host.Create(HList.Cons(new B(4), HList.From(new A(3))));
        entity.Remove<B>();
        Assert.Equal(1, inner.RemoveCalls);
        Assert.Equal(3, entity.Get<A>().Value);
    }
}
