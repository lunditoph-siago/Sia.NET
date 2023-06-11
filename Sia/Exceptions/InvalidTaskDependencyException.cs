namespace Sia;

[System.Serializable]
public class InvalidTaskDependencyException : SchedulerException
{
    public InvalidTaskDependencyException() { }
    public InvalidTaskDependencyException(string message) : base(message) { }
    public InvalidTaskDependencyException(string message, System.Exception inner) : base(message, inner) { }
    protected InvalidTaskDependencyException(
        System.Runtime.Serialization.SerializationInfo info,
        System.Runtime.Serialization.StreamingContext context) : base(info, context) { }
}