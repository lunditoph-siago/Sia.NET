using Microsoft.VisualBasic;

namespace Sia;

public record DynBundle : IBundle
{
    private interface IBundleImpl : IBundle
    {
        void Add<T, THandler>(in T component, in THandler handler)
            where THandler : IGenericHandler<IHList>;

        void Concat<THList, THandler>(in THList list, in THandler handler)
            where THList : IHList
            where THandler : IGenericHandler<IHList>;

        bool Remove<TValue, THandler>(TypeProxy<TValue> proxy, in THandler handler)
            where THandler : IGenericHandler<IHList>;

        bool Remove<TValue, THandler>(in TValue value, in THandler handler)
            where TValue : IEquatable<TValue>
            where THandler : IGenericHandler<IHList>; 
    }

    private class BundleImpl<THList>(in THList list) : IBundleImpl
        where THList : IHList
    {
        private readonly THList _list = list;

        public void ToHList<THandler>(in THandler handler)
            where THandler : IGenericHandler<IHList>
            => handler.Handle(_list);

        public void HandleHListType<THandler>(in THandler handler)
            where THandler : IGenericTypeHandler<IHList>
            => handler.Handle<THList>();

        public void Add<T, THandler>(in T component, in THandler handler)
            where THandler : IGenericHandler<IHList>
            => handler.Handle(HList.Cons(component, _list));

        public void Concat<THList1, THandler>(in THList1 list, in THandler handler)
            where THList1 : IHList
            where THandler : IGenericHandler<IHList>
            => _list.Concat(list, handler);

        public bool Remove<TValue, THandler>(TypeProxy<TValue> proxy, in THandler handler)
            where THandler : IGenericHandler<IHList>
            => _list.Remove(proxy, handler);

        public bool Remove<TValue, THandler>(in TValue value, in THandler handler)
            where TValue : IEquatable<TValue>
            where THandler : IGenericHandler<IHList>
            => _list.Remove(value, handler);
    }

#pragma warning disable CS8500 // This takes the address of, gets the size of, or declares a pointer to a managed type

    private unsafe struct BundleImplCreator(IBundleImpl* result)
        : IGenericHandler<IHList>
    {
        public readonly void Handle<T>(in T value) where T : IHList
            => *result = new BundleImpl<T>(value);
    }

    private unsafe struct BundleAdder(IBundleImpl impl, IBundleImpl* result)
        : IGenericHandler<IHList>
    {
        public readonly void Handle<T>(in T value)
            where T : IHList
            => impl.Concat(value, new BundleImplCreator(result));
    }

    private unsafe struct TypeRemover<TList>(IBundleImpl* impl)
        : IGenericTypeHandler
        where TList : IHList
    {
        public readonly void Handle<T>()
            => (*impl).Remove(TypeProxy<T>.Default, new BundleImplCreator(impl));
    }

    private unsafe struct HListRemover<TList>(IBundleImpl* result)
        : IGenericHandler<IHList>
        where TList : IHList
    {
        public readonly void Handle<T>(in T value)
            where T : IHList
            => TList.HandleTypes(new TypeRemover<T>(result));
    }

    private readonly IBundleImpl _bundleImpl;

    public DynBundle()
    {
        _bundleImpl = new BundleImpl<EmptyHList>(EmptyHList.Default);
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
        where THList : IHList
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
        _bundleImpl.Remove(TypeProxy<TComponent>.Default, new BundleImplCreator(&impl));
        return new(impl!);
    }

    public unsafe DynBundle RemoveMany<TList>()
        where TList : IHList
    {
        var impl = _bundleImpl;
        _bundleImpl.ToHList(new HListRemover<TList>(&impl));
        return new(impl);
    }

#pragma warning restore CS8500 // This takes the address of, gets the size of, or declares a pointer to a managed type

    public void ToHList<THandler>(in THandler handler)
        where THandler : IGenericHandler<IHList>
        => _bundleImpl.ToHList(handler);

    public void HandleHListType<THandler>(in THandler handler)
        where THandler : IGenericTypeHandler<IHList>
        => _bundleImpl.HandleHListType(handler);
}