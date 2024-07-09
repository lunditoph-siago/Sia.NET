namespace Sia;

public record DynBundle : IBundle
{
    private interface IBundleImpl : IBundle
    {
        void Add<T, THandler>(in T component, in THandler handler)
            where THandler : IGenericStructHandler<IHList>;

        void Concat<THList, THandler>(in THList list, in THandler handler)
            where THList : struct, IHList
            where THandler : IGenericStructHandler<IHList>;

        bool Remove<TValue, THandler>(TypeProxy<TValue> proxy, in THandler handler)
            where THandler : IGenericStructHandler<IHList>;

        bool Remove<TValue, THandler>(in TValue value, in THandler handler)
            where TValue : IEquatable<TValue>
            where THandler : IGenericStructHandler<IHList>; 
    }

    private class BundleImpl<THList>(in THList list) : IBundleImpl
        where THList : struct, IHList
    {
        private readonly THList _list = list;

        public void ToHList<THandler>(in THandler handler)
            where THandler : IGenericStructHandler<IHList>
            => handler.Handle(_list);

        public void HandleHListType<THandler>(in THandler handler)
            where THandler : IGenericStructTypeHandler<IHList>
            => handler.Handle<THList>();

        public void Add<T, THandler>(in T component, in THandler handler)
            where THandler : IGenericStructHandler<IHList>
            => handler.Handle(HList.Cons(component, _list));

        public void Concat<THList1, THandler>(in THList1 list, in THandler handler)
            where THList1 : struct, IHList
            where THandler : IGenericStructHandler<IHList>
            => _list.Concat(list, handler);

        public bool Remove<TValue, THandler>(TypeProxy<TValue> proxy, in THandler handler)
            where THandler : IGenericStructHandler<IHList>
            => _list.Remove(proxy, handler);

        public bool Remove<TValue, THandler>(in TValue value, in THandler handler)
            where TValue : IEquatable<TValue>
            where THandler : IGenericStructHandler<IHList>
            => _list.Remove(value, handler);
    }

#pragma warning disable CS8500 // This takes the address of, gets the size of, or declares a pointer to a managed type

    private unsafe struct BundleImplCreator(IBundleImpl* result)
        : IGenericStructHandler<IHList>
    {
        public readonly void Handle<T>(in T value)
            where T : struct, IHList
            => *result = new BundleImpl<T>(value);
    }

    private unsafe struct BundleAdder(IBundleImpl impl, IBundleImpl* result)
        : IGenericStructHandler<IHList>
    {
        public readonly void Handle<T>(in T value)
            where T : struct, IHList
            => impl.Concat(value, new BundleImplCreator(result));
    }

    private unsafe struct TypeRemover<TList>(IBundleImpl* impl)
        : IGenericTypeHandler
        where TList : struct, IHList
    {
        public readonly void Handle<T>()
            => (*impl).Remove(TypeProxy<T>._, new BundleImplCreator(impl));
    }

    private unsafe struct HListRemover<TList>(IBundleImpl* result)
        : IGenericStructHandler<IHList>
        where TList : struct, IHList
    {
        public readonly void Handle<T>(in T value)
            where T : struct, IHList
            => TList.HandleTypes(new TypeRemover<T>(result));
    }

    private readonly IBundleImpl _bundleImpl;

    public DynBundle()
    {
        _bundleImpl = new BundleImpl<EmptyHList>(EmptyHList.Default);
    }

    public unsafe DynBundle(params IBundle[] bundles)
    {
        IBundleImpl impl = new BundleImpl<EmptyHList>(EmptyHList.Default);

        foreach (var bundle in bundles) {
            bundle.ToHList(new BundleAdder(impl, &impl));
        }

        _bundleImpl = impl;
    }

    public unsafe DynBundle(IEnumerable<IBundle> bundles)
    {
        IBundleImpl impl = new BundleImpl<EmptyHList>(EmptyHList.Default);

        foreach (var bundle in bundles) {
            bundle.ToHList(new BundleAdder(impl, &impl));
        }

        _bundleImpl = impl;
    }

    private DynBundle(IBundleImpl bundleImpl)
    {
        _bundleImpl = bundleImpl;
    }

    public DynBundle Add<TComponent>() => Add<TComponent>(default!);

    public unsafe DynBundle Add<TComponent>(in TComponent initial)
    {
        IBundleImpl? impl = null;
        _bundleImpl.Add(initial, new BundleImplCreator(&impl));
        return new(impl!);
    }

    public unsafe DynBundle AddMany<THList>(in THList list)
        where THList : struct, IHList
    {
        IBundleImpl? impl = null;
        _bundleImpl.Concat(list, new BundleImplCreator(&impl));
        return new(impl!);
    }

    public unsafe DynBundle AddBundle<TBundle>(in TBundle other)
        where TBundle : IBundle
    {
        IBundleImpl? impl = null;
        other.ToHList(new BundleAdder(_bundleImpl, &impl));
        return new(impl!);
    }

    public unsafe DynBundle Remove<TComponent>()
    {
        IBundleImpl? impl = null;
        _bundleImpl.Remove(TypeProxy<TComponent>._, new BundleImplCreator(&impl));
        return new(impl!);
    }

    public unsafe DynBundle RemoveMany<TList>()
        where TList : struct, IHList
    {
        var impl = _bundleImpl;
        _bundleImpl.ToHList(new HListRemover<TList>(&impl));
        return new(impl);
    }

#pragma warning restore CS8500 // This takes the address of, gets the size of, or declares a pointer to a managed type

    public void ToHList<THandler>(in THandler handler)
        where THandler : IGenericStructHandler<IHList>
        => _bundleImpl.ToHList(handler);

    public void HandleHListType<THandler>(in THandler handler)
        where THandler : IGenericStructTypeHandler<IHList>
        => _bundleImpl.HandleHListType(handler);
}