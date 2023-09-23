namespace Sia;

public abstract class SystemBase : ISystem
{
    public ISystemUnion? Children { get; init; }
    public ISystemUnion? Dependencies { get; init; }
    public IEntityMatcher? Matcher { get; init; }
    public IEventUnion? Trigger { get; init; }
    public IEventUnion? Filter { get; init; }

    public virtual void Initialize(World world, Scheduler scheduler) {}
    public virtual void Uninitialize(World world, Scheduler scheduler) {}

    public virtual void Execute(World world, Scheduler scheduler, IEntityQuery query) {}

    public virtual bool OnTriggerEvent<TEvent>(World world, Scheduler scheduler, in EntityRef entity, in TEvent e)
        where TEvent : IEvent
        => true;

    public virtual bool OnFilterEvent<TEvent>(World world, Scheduler scheduler, in EntityRef entity, in TEvent e)
        where TEvent : IEvent
        => true;
}