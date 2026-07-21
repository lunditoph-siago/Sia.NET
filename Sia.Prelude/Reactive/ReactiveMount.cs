namespace Sia.Reactive;

public readonly struct ReactiveMount<TProps, TState, TMessage>
    where TProps : struct
    where TState : struct
{
    private readonly MountHandle<FunctionalSpec<TProps, TState, TMessage>> _handle;

    internal ReactiveMount(
        MountHandle<FunctionalSpec<TProps, TState, TMessage>> handle)
        => _handle = handle;

    public bool IsMounted => _handle.IsMounted;
    public TProps Props => _handle.Props.Props;
    public TState State => _handle.Cell.GetUnchecked<TState>();

    public void Update(scoped in TProps props)
    {
        var current = _handle.Props;
        _handle.Update(new(current.Component, props));
    }

    public void Dispatch(scoped in TMessage message)
        => _handle.Owner.DispatchMessage(
            _handle.Cell,
            _handle.Identity,
            message!);

    public void Invalidate()
        => _handle.Invalidate();

    public void Unmount()
        => _handle.Unmount();
}

public static class ReactiveWorldExtensions
{
    extension(World world)
    {
        public ReactiveMount<TProps, TState, TMessage> Mount<
            TProps, TState, TMessage>(
            ReactiveComponent<TProps, TState, TMessage> component,
            scoped in TProps props)
            where TProps : struct
            where TState : struct
        {
            ArgumentNullException.ThrowIfNull(component);
            var reconciler = world.AcquireAddon<Reconciler>();
            var handle = reconciler.Mount(
                new FunctionalSpec<TProps, TState, TMessage>(component, props));
            return new(handle);
        }

        public void FlushReactive()
            => world.AcquireAddon<Reconciler>().Flush();
    }
}
