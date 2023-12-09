namespace Sia;

public abstract class AddonSystemBase : SystemBase
{
    private readonly Dictionary<Type, Action<World>> _addonRemovers = [];

    protected TAddon AddAddon<TAddon>(World world)
        where TAddon : IAddon, new()
    {
        _addonRemovers.TryAdd(typeof(TAddon), static world => world.RemoveAddon<TAddon>());
        return world.AcquireAddon<TAddon>();
    }

    public override void Uninitialize(World world, Scheduler scheduler)
    {
        foreach (var remover in _addonRemovers.Values) {
            remover(world);
        }
        _addonRemovers.Clear();
    }
}