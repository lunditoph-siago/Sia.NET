namespace Sia;

[Serializable]
public class InvalidTaskDependencyException : SchedulerException
{
    public InvalidTaskDependencyException() { }
    public InvalidTaskDependencyException(string message) : base(message) { }
    public InvalidTaskDependencyException(string message, Exception inner) : base(message, inner) { }
}