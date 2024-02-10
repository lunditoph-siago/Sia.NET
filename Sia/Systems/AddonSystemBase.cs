namespace Sia;

public abstract class AddonSystemBase : SystemBase
{
    private readonly Dictionary<Type, Action<World>> _addonRemovers = [];

    public AddonSystemBase() {}
    public AddonSystemBase(
        SystemChain? children = null, IEntityMatcher? matcher = null,
        IEventUnion? trigger = null, IEventUnion? filter = null)
    {
        Children = children;
        Matcher = matcher;
        Trigger = trigger;
        Filter = filter;
    }

    protected TAddon AddAddon<TAddon>(World world)
        where TAddon : class, IAddon, new()
    {
        _addonRemovers.TryAdd(typeof(TAddon),
            static world => world.RemoveAddon<TAddon>());
        return world.AddAddon<TAddon>();
    }

    public override void Uninitialize(World world, Scheduler scheduler)
    {
        foreach (var remover in _addonRemovers.Values) {
            remover(world);
        }
        _addonRemovers.Clear();
    }
}