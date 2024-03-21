namespace Sia;

[Serializable]
public class TaskDependedException : SchedulerException
{
    public TaskDependedException() {}
    public TaskDependedException(string message) : base(message) {}
    public TaskDependedException(string message, Exception inner) : base(message, inner) {}
}