namespace Sia;

public interface IScheduleEntry
{
    void OnAttached(Scheduler scheduler, ScheduleLabel label) { }

    void Tick();

    void OnDetached(Scheduler scheduler, ScheduleLabel label) { }
}
