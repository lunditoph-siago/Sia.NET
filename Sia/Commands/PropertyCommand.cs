namespace Sia;

public abstract class PropertyCommand<TCommand, TTarget, T>
    : SingleValuePooledEvent<TCommand, T>, ICommand<TTarget>
    where TCommand : ICommand<TTarget>, new()
{
    public abstract void Execute(in TTarget target);
}

public abstract class PropertyCommand<TCommand, T>
    : PropertyCommand<TCommand, EntityRef, T>, ICommand
    where TCommand : ICommand<EntityRef>, new()
{
}