namespace Sia;

public record DynBundle : IBundle
{
    private class BundleImpl<THList>(in THList list) : IBundle
        where THList : IHList
    {
        private readonly THList _list = list;

        public void ToHList<THandler>(in THandler handler)
            where THandler : IGenericHandler<IHList>
            => handler.Handle(_list);

        public void HandleHListType<THandler>(in THandler handler)
            where THandler : IGenericTypeHandler<IHList>
            => handler.Handle<THList>();
    }

#pragma warning disable CS8500 // This takes the address of, gets the size of, or declares a pointer to a managed type

    private unsafe struct BundleCreator(IBundle* result)
        : IGenericHandler<IHList>
    {
        public readonly void Handle<T>(in T value) where T : IHList
            => *result = new BundleImpl<T>(value);
    }

    private unsafe struct ComponentAdder<TComponent>(IBundle* result, in TComponent initial)
        : IGenericHandler<IHList>
    {
        private readonly TComponent _initial = initial;

        public readonly void Handle<T>(in T value)
            where T : IHList
            => *result = new BundleImpl<HList<TComponent, T>>(HList.Cons(_initial, value));
    }

    private unsafe struct ComponentConcater<THList>(IBundle* result, in THList list)
        : IGenericHandler<IHList>
        where THList : IHList
    {
        private readonly THList _list = list;

        public readonly void Handle<T>(in T value)
            where T : IHList
            => _list.Concat(value, new BundleCreator(result));
    }

    private unsafe struct BundleAdder(IBundle prevBundle, IBundle* result) : IGenericHandler<IHList>
    {
        public readonly void Handle<T>(in T value)
            where T : IHList
            => prevBundle.ToHList(new ComponentConcater<T>(result, value));
    }

    private unsafe struct ComponentRemover<TComponent>(IBundle* result)
        : IGenericHandler<IHList>
    {
        public readonly void Handle<T>(in T value)
            where T : IHList
            => value.Remove(TypeProxy<TComponent>.Default, new BundleCreator(result));
    }

    private IBundle _bundleImpl = new BundleImpl<EmptyHList>(EmptyHList.Default);

    public DynBundle Add<TComponent>() => Add<TComponent>(default!);

    public unsafe DynBundle Add<TComponent>(in TComponent initial)
    {
        var bundle = new DynBundle();
        fixed (IBundle* bundlePtr = &bundle._bundleImpl) {
            _bundleImpl.ToHList(new ComponentAdder<TComponent>(bundlePtr, initial));
        }
        return bundle;
    }

    public unsafe DynBundle AddMany<THList>(in THList list)
        where THList : IHList
    {
        var bundle = new DynBundle();
        fixed (IBundle* bundlePtr = &bundle._bundleImpl) {
            _bundleImpl.ToHList(new ComponentConcater<THList>(bundlePtr, list));
        }
        return bundle;
    }

    public unsafe DynBundle AddBundle<TBundle>(in TBundle other)
        where TBundle : IBundle
    {
        var bundle = new DynBundle();
        fixed (IBundle* bundlePtr = &bundle._bundleImpl) {
            other.ToHList(new BundleAdder(_bundleImpl, bundlePtr));
        }
        return bundle;
    }

    public unsafe DynBundle Remove<TComponent>()
    {
        var bundle = new DynBundle();
        fixed (IBundle* bundlePtr = &bundle._bundleImpl) {
            _bundleImpl.ToHList(new ComponentRemover<TComponent>(bundlePtr));
        }
        return bundle;
    }

#pragma warning restore CS8500 // This takes the address of, gets the size of, or declares a pointer to a managed type

    public void ToHList<THandler>(in THandler handler)
        where THandler : IGenericHandler<IHList>
        => _bundleImpl.ToHList(handler);

    public void HandleHListType<THandler>(in THandler handler)
        where THandler : IGenericTypeHandler<IHList>
        => _bundleImpl.HandleHListType(handler);
}