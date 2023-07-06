namespace Sia;

public abstract class Command<TCommand, TTarget>
    : PooledEvent<TCommand>, ICommand<TTarget>
    where TCommand : ICommand<TTarget>, new()
{
    public abstract void Execute(in TTarget target);
}

public abstract class Command<TCommand>
    : Command<TCommand, EntityRef>, ICommand
    where TCommand : ICommand<EntityRef>, new()
{
}