namespace Sia;

public static class WorldSystemExtensions
{
    public static SystemHandle RegisterSystem<TSystem>(
        this World world, Scheduler scheduler, Scheduler.TaskGraphNode[]? dependedTasks = null)
        where TSystem : ISystem, new()
        => world.AcquireAddon<SystemLibrary>().Register<TSystem>(scheduler, dependedTasks);
}