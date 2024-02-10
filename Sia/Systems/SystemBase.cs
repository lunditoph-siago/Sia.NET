namespace Sia;

public abstract class SystemBase(
    SystemChain? children = null, IEntityMatcher? matcher = null,
    IEventUnion? trigger = null, IEventUnion? filter = null) : ISystem
{
    public SystemChain? Children { get; init; } = children;
    public IEntityMatcher? Matcher { get; init; } = matcher;
    public IEventUnion? Trigger { get; init; } = trigger;
    public IEventUnion? Filter { get; init; } = filter;

    public virtual void Initialize(World world, Scheduler scheduler) {}
    public virtual void Uninitialize(World world, Scheduler scheduler) {}
    public virtual void Execute(World world, Scheduler scheduler, IEntityQuery query) {}
}