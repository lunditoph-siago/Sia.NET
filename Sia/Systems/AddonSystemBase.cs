namespace Sia;

public abstract class AddonSystemBase : SystemBase
{
    private readonly Dictionary<Type, Action<World>> _addonRemovers = new();

    protected void AddAddon<TAddon>(World world)
        where TAddon : IAddon, new()
    {
        if (!_addonRemovers.TryAdd(typeof(TAddon), static world => world.RemoveAddon<TAddon>())) {
            return;
        }
        world.AcquireAddon<TAddon>();
    }

    public override void Uninitialize(World world, Scheduler scheduler)
    {
        foreach (var remover in _addonRemovers.Values) {
            remover(world);
        }
        _addonRemovers.Clear();
    }
}