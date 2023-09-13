namespace Sia;

public interface ICommand<TTarget> : IEvent
    where TTarget : notnull
{
    void Execute(World<TTarget> world, in TTarget target);
}