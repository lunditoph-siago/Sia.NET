namespace Sia;

public interface ISystem
{
    ISystemUnion? Children { get; }
    ISystemUnion? Dependencies { get; }
    IMatcher? Matcher { get; }
    IEventUnion? Trigger { get; }

    SystemHandle Register(
        World<EntityRef> world, Scheduler scheduler, Scheduler.TaskGraphNode[]? dependedTasks = null);
}