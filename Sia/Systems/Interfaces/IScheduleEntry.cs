namespace Sia;

public interface IScheduleEntry
{
    void OnAttached(Scheduler scheduler, ScheduleLabel label) { }

    void Tick();

    void OnDetached(Scheduler scheduler, ScheduleLabel label) { }
}

public interface ISystemScheduleEntry : IScheduleEntry
{
    int Version { get; }
    ExecutionPlan? Plan { get; }

    void TickSystem(int index);
}
