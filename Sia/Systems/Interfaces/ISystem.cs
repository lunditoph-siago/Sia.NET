namespace Sia;

public interface ISystem
{
    ISystemUnion? Children { get; }
    ISystemUnion? Dependencies { get; }
    ITypeUnion? Matcher { get; }
    ICommandUnion? Trigger { get; }

    SystemHandle Register(
        World<EntityRef> world, Scheduler scheduler, Scheduler.TaskGraphNode[]? dependedTasks = null);
}