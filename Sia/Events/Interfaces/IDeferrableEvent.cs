namespace Sia;

public interface IDeferrableEvent<TTarget> : IEvent
{
    bool ShouldDefer(in TTarget target);
}