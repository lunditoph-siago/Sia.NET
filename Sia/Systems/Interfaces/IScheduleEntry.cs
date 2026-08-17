namespace Sia;

public interface IScheduleEntry
{
    void OnAttached(Scheduler scheduler, ScheduleLabel label) { }

    void Tick(WorldContext context);

    void OnDetached(Scheduler scheduler, ScheduleLabel label) { }
}

public interface ISystemScheduleEntry : IScheduleEntry
{
    int Version { get; }
    ExecutionPlan? Plan { get; }

    void TickSystem(int index, WorldContext context);

    void IScheduleEntry.Tick(WorldContext context)
    {
        var count = Plan?.Entries.Length ?? 0;
        for (var index = 0; index < count; index++) {
            TickSystem(index, context);
        }
    }
}
