namespace Sia;

public abstract class SystemBase : ISystem
{
    public SystemChain? Children { get; init; }
    public IEntityMatcher? Matcher { get; init; }
    public IEventUnion? Trigger { get; init; }
    public IEventUnion? Filter { get; init; }

    public SystemBase() {}
    public SystemBase(
        SystemChain? children = null, IEntityMatcher? matcher = null,
        IEventUnion? trigger = null, IEventUnion? filter = null)
    {
        Children = children;
        Matcher = matcher;
        Trigger = trigger;
        Filter = filter;
    }

    public virtual void Initialize(World world, Scheduler scheduler) {}
    public virtual void Uninitialize(World world, Scheduler scheduler) {}
    public virtual void Execute(World world, Scheduler scheduler, IEntityQuery query) {}
}