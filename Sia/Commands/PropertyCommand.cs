namespace Sia;

public abstract record PropertyCommand<TCommand, T>
    : SingleValuePooledCommand<TCommand, T>, IExecutable
    where TCommand : IExecutable, new()
{
    public abstract void Execute(EntityRef target);
}