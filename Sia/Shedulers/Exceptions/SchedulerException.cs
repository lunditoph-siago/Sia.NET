namespace Sia;

[Serializable]
public class SchedulerException : Exception
{
    public SchedulerException() { }
    public SchedulerException(string message) : base(message) { }
    public SchedulerException(string message, Exception inner) : base(message, inner) { }
    protected SchedulerException(
        System.Runtime.Serialization.SerializationInfo info,
        System.Runtime.Serialization.StreamingContext context) : base(info, context) { }
}