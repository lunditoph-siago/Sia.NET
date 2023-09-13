namespace Sia;

public interface IDeferrableCommand<TTarget> : ICommand<TTarget>
    where TTarget : notnull
{
    bool ShouldDefer(in TTarget target);
}