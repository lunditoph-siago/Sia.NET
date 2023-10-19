namespace Sia;

using System.Diagnostics.CodeAnalysis;

public abstract class ViewBase<TTypeUnion> : IAddon
    where TTypeUnion : ITypeUnion, new()
{
    [AllowNull]
    public World World { get; private set; }

    [AllowNull]
    protected World.EntityQuery Query { get; private set; }

    public virtual void OnInitialize(World world)
    {
        World = world;
        Query = world.Query<TTypeUnion>();
        Query.OnEntityHostAdded += OnEntityHostAdded;

        foreach (var host in Query.Hosts) {
            OnEntityHostAdded(host);
        }
    }

    private void OnEntityHostAdded(IReactiveEntityHost host)
    {
        host.OnEntityCreated += OnEntityAdded;
        host.OnEntityReleased += OnEntityRemoved;

        host.IterateAllocated((this, host),
            static (in (ViewBase<TTypeUnion> View, IReactiveEntityHost Host) data, long pointer) =>
                data.View.OnEntityAdded(new(pointer, data.Host)));
    }

    public virtual void OnUninitialize(World world)
    {
        foreach (var host in Query.Hosts) {
            host.OnEntityCreated -= OnEntityAdded;
            host.OnEntityReleased -= OnEntityRemoved;
        }
        Query.Dispose();
        Query = null;
    }

    protected abstract void OnEntityAdded(in EntityRef entity);
    protected abstract void OnEntityRemoved(in EntityRef entity);
}