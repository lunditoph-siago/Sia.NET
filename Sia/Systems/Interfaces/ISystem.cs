namespace Sia;

public interface ISystem
{
    ISystemUnion? Children => null;
    ISystemUnion? Dependencies => null;
    IEntityMatcher? Matcher => null;
    IEventUnion? Trigger => null;
    IEventUnion? Filter => null;

    void Initialize(World world, Scheduler scheduler) {}
    void Uninitialize(World world, Scheduler scheduler) {}

    void BeforeExecute(World world, Scheduler scheduler) {}
    void AfterExecute(World world, Scheduler scheduler) {}
    void Execute(World world, Scheduler scheduler, in EntityRef entity) {}

    bool OnTriggerEvent<TEvent>(World world, Scheduler scheduler, in EntityRef entity, in TEvent e)
        where TEvent : IEvent
        => true;

    bool OnFilterEvent<TEvent>(World world, Scheduler scheduler, in EntityRef entity, in TEvent e)
        where TEvent : IEvent
        => true;
}