namespace Sia;

public interface IScheduleSource
{
    void OnBeginTick();
    void OnBeforeSchedule(ScheduleLabel label) { }
}
