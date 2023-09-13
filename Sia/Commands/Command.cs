namespace Sia;

public abstract class ImpureCommand<TCommand, TTarget>
    : PooledEvent<TCommand>, ICommand<TTarget>
    where TCommand : ImpureCommand<TCommand, TTarget>, new()
    where TTarget : notnull
{
    public abstract void Execute(World<TTarget> world, in TTarget target);
}

public abstract class Command<TCommand, TTarget>
    : PooledEvent<TCommand>, ICommand<TTarget>
    where TCommand : Command<TCommand, TTarget>, new()
    where TTarget : notnull
{
    public void Execute(World<TTarget> world, in TTarget target)
        => Execute(target);
    
    public abstract void Execute(in TTarget target);
}