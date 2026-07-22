namespace Sia.Reactive;

public readonly struct ReactiveMount<TProps>
    where TProps : struct
{
    private readonly MountHandle<ComponentSpec<TProps>> _handle;

    internal ReactiveMount(MountHandle<ComponentSpec<TProps>> handle)
        => _handle = handle;

    public bool IsMounted => _handle.IsMounted;
    public TProps Props => _handle.Props.Props;

    public void Update(scoped in TProps props)
    {
        var current = _handle.Props;
        _handle.Update(new(current.Render, props));
    }

    public void Invalidate()
        => _handle.Invalidate();

    public void Unmount()
        => _handle.Unmount();
}

public static class ReactiveWorldExtensions
{
    extension(World world)
    {
        public ReactiveMount<TProps> Mount<TProps>(
            [NestedCallback] ReactiveComponent<TProps> render,
            scoped in TProps props)
            where TProps : struct
        {
            ArgumentNullException.ThrowIfNull(render);
            var reconciler = world.AcquireAddon<Reconciler>();
            var handle = reconciler.Mount(
                new ComponentSpec<TProps>(render, props));
            return new(handle);
        }

        public void FlushReactive()
            => world.AcquireAddon<Reconciler>().Flush();
    }
}
