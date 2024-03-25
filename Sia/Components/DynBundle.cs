namespace Sia;

public class DynBundle : IBundle
{
    private class BundleImpl<THList>(in THList list) : IBundle
        where THList : IHList
    {
        private readonly THList _list = list;

        public void ToHList(IGenericHandler<IHList> handler)
            => handler.Handle(_list);
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

    private unsafe struct BundleAdder(IBundle* result) : IGenericHandler<IHList>
    {
        public readonly void Handle<T>(in T value)
            where T : IHList
            => (*result).ToHList(new ComponentConcater<T>(result, value));
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
        fixed (IBundle* bundlePtr = &_bundleImpl) {
            _bundleImpl.ToHList(new ComponentAdder<TComponent>(bundlePtr, initial));
        }
        return this;
    }

    public unsafe DynBundle AddMany<THList>(in THList list)
        where THList : IHList
    {
        fixed (IBundle* bundlePtr = &_bundleImpl) {
            _bundleImpl.ToHList(new ComponentConcater<THList>(bundlePtr, list));
        }
        return this;
    }

    public unsafe DynBundle AddBundle<TBundle>(in TBundle bundle)
        where TBundle : IBundle
    {
        fixed (IBundle* bundlePtr = &_bundleImpl) {
            bundle.ToHList(new BundleAdder(bundlePtr));
        }
        return this;
    }

    public unsafe DynBundle Remove<TComponent>()
    {
        fixed (IBundle* bundlePtr = &_bundleImpl) {
            _bundleImpl.ToHList(new ComponentRemover<TComponent>(bundlePtr));
        }
        return this;
    }

#pragma warning restore CS8500 // This takes the address of, gets the size of, or declares a pointer to a managed type

    public void ToHList(IGenericHandler<IHList> handler)
        => _bundleImpl.ToHList(handler);
}