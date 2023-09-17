namespace Sia;

public interface ISystem
{
    ISystemUnion? Children { get; }
    ISystemUnion? Dependencies { get; }
    IEntityMatcher? Matcher { get; }
    IEventUnion? Trigger { get; }

    SystemHandle Register(World world, Scheduler scheduler, Scheduler.TaskGraphNode[]? dependedTasks = null);
}