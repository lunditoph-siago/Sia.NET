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

    public State<T> GetState<T>(int index = 0)
        where T : struct
    {
        var cell = _handle.Cell.GetUnchecked<Cell>();
        var states = cell.States
            ?? throw new InvalidOperationException(
                $"Component has no state cells. "
                + "Ensure hooks.UseState is called unconditionally in Render.");
        return new State<T>(
            states.PeekState<T>(index),
            _handle.Owner,
            _handle.Cell,
            _handle.Identity);
    }
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
