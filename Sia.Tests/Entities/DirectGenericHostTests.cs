namespace Sia.Tests.Entities;

public sealed class DirectGenericHostTests
{
    public struct A { public int Value; }
    public struct B { public int Value; }
    public sealed class TrackingArray<T> : ArrayEntityHost<T> where T : struct, IHList
    {
        public int Adds;
        public int Removes;
        public override void Add<TComponent>(Entity entity, in TComponent initial) { Adds++; base.Add(entity, initial); }
        public override void Remove<TComponent>(Entity entity, out bool success) { Removes++; base.Remove<TComponent>(entity, out success); }
    }
    public struct StructHost<T> : IEntityHost<T> where T : struct, IHList
    {
        private readonly TrackingArray<T> inner;
        public StructHost() => inner = new();
        public readonly int Adds => inner.Adds;
        public readonly int Removes => inner.Removes;
        public event Action<IEntityHost>? OnDisposed { add => inner.OnDisposed += value; remove => inner.OnDisposed -= value; }
        public readonly Type EntityType => inner.EntityType;
        public readonly EntityDescriptor Descriptor => inner.Descriptor;
        public readonly World? World => inner.World;
        public readonly int Capacity => inner.Capacity;
        public readonly int Count => inner.Count;
        public readonly int Version => inner.Version;
        public readonly void OnInitialize(World world) => ((IEntityHost)inner).OnInitialize(world);
        public readonly Entity Create() => inner.Create();
        public readonly Entity Create(in T value) => inner.Create(value);
        public readonly void Release(Entity e) => inner.Release(e);
        public readonly void MoveOut(Entity e, IEntityHost destination) => inner.MoveOut(e, destination);
        public readonly void MoveIn(Entity e, in T value, IEntityHost source) => inner.MoveIn(e, value, source);
        public readonly Entity GetEntity(int slot) => inner.GetEntity(slot);
        public readonly ref byte GetByteRef(int slot) => ref inner.GetByteRef(slot);
        public readonly ref T GetRef(int slot) => ref inner.GetRef(slot);
        public readonly void GetHList<H>(int slot, in H handler) where H : IRefGenericHandler<IHList> => inner.GetHList(slot, handler);
        public readonly void Add<C>(Entity e, in C value) => inner.Add(e, value);
        public readonly void AddMany<L>(Entity e, in L list) where L : struct, IHList => inner.AddMany(e, list);
        public readonly void Set<C>(Entity e, in C value) => inner.Set(e, value);
        public readonly void Remove<C>(Entity e, out bool success) => inner.Remove<C>(e, out success);
        public readonly void RemoveMany<L>(Entity e) where L : struct, IHList => inner.RemoveMany<L>(e);
        public readonly object Box(int slot) => inner.Box(slot);
        public readonly IEntityHost<U> GetSiblingHost<U>() where U : struct, IHList => inner.GetSiblingHost<U>();
        public readonly void GetSiblingHostType<U,H>(in H handler) where U : struct, IHList where H : IGenericConcreteTypeHandler<IEntityHost<U>> => inner.GetSiblingHostType<U,H>(handler);
        public readonly Span<Entity> UnsafeGetEntitySpan() => inner.UnsafeGetEntitySpan();
        public readonly IEnumerator<Entity> GetEnumerator() => inner.GetEnumerator();
        readonly System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        public readonly void Dispose() => inner.Dispose();
    }
    [Fact]
    public void Struct_inner_host_keeps_add_and_remove_dispatch()
    {
        using(var world = new World()) {
            var host=world.AddHost<HList<A, EmptyHList>, StructHost<HList<A, EmptyHList>>>();
            var e=host.Create(HList.From(new A {Value=3}));
            e.Add(new B {Value=4});
            Assert.Equal(1, host.InnerHost.Adds);
            Assert.Equal(4, e.Get<B>().Value);
        }
        using(var world = new World()) {
            var host=world.AddHost<HList<B, HList<A, EmptyHList>>, StructHost<HList<B, HList<A, EmptyHList>>>>();
            var e=host.Create(HList.Cons(new B {Value=4},HList.From(new A {Value=3})));
            e.Remove<B>();
            Assert.Equal(1, host.InnerHost.Removes);
            Assert.Equal(3, e.Get<A>().Value);
        }
    }
}
