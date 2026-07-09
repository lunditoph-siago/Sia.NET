namespace Sia;

public abstract class ScheduleRegistration : IDisposable
{
    public abstract bool IsAttached { get; }

    internal ScheduleRegistration() { }

    public abstract void Dispose();
}
