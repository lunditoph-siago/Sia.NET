namespace Sia;

using System.Collections.Concurrent;

public abstract record PooledCommand<TCommand> : Command
    where TCommand : ICommand, new()
{
    private static ConcurrentStack<ICommand> s_pool = new();

    public bool IsDisposed { get; private set; }

    protected static TCommand Create()
    {
        if (s_pool.TryPop(out var command)) {
            ((PooledCommand<TCommand>)command).IsDisposed = false;
            return (TCommand)command;
        }
        return new TCommand();
    }

    public override void Dispose()
    {
        if (IsDisposed) {
            return;
        }
        s_pool.Push(this);
        IsDisposed = true;
    }
}