namespace Sia;

public abstract class PropertyCommand<TCommand, TTarget, TValue>
    : SingleValuePooledEvent<TCommand, TValue>, ICommand<TTarget>
    where TCommand : ICommand<TTarget>, new()
{
    public abstract void Execute(in TTarget target);
}

public abstract class PropertyCommand<TCommand, TValue>
    : PropertyCommand<TCommand, EntityRef, TValue>, ICommand
    where TCommand : ICommand<EntityRef>, new()
{
}