namespace Sia;

public interface ISystem
{
    SystemChain? Children { get; }
    IEntityMatcher? Matcher { get; }
    IEventUnion? Trigger { get; }
    IEventUnion? Filter { get; }

    void Initialize(World world, Scheduler scheduler);
    void Uninitialize(World world, Scheduler scheduler);
    void Execute(World world, Scheduler scheduler, IEntityQuery query);
}