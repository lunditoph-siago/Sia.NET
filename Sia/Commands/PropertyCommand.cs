namespace Sia;

public abstract class ImpurePropertyCommand<TCommand, TTarget, TValue>
    : SingleValuePooledEvent<TCommand, TValue>, ICommand<TTarget>
    where TCommand : ICommand<TTarget>, new()
    where TTarget : notnull
{
    public abstract void Execute(World<TTarget> world, in TTarget target);
}

public abstract class ImpurePropertyCommand<TCommand, TValue>
    : ImpurePropertyCommand<TCommand, EntityRef, TValue>, ICommand
    where TCommand : ICommand<EntityRef>, new()
{
}

public abstract class PropertyCommand<TCommand, TTarget, TValue>
    : SingleValuePooledEvent<TCommand, TValue>, ICommand<TTarget>
    where TCommand : ICommand<TTarget>, new()
    where TTarget : notnull
{
    public void Execute(World<TTarget> world, in TTarget target)
        => Execute(target);
    
    public abstract void Execute(in TTarget target);
}

public abstract class PropertyCommand<TCommand, TValue>
    : PropertyCommand<TCommand, EntityRef, TValue>, ICommand
    where TCommand : ICommand<EntityRef>, new()
{
}