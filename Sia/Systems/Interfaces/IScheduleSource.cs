namespace Sia;

public interface IScheduleSource
{
    void OnAttached(Scheduler scheduler) { }

    void OnBeginTick();

    void OnBeforeSchedule(ScheduleLabel label) { }

    void OnDetached(Scheduler scheduler) { }
}
