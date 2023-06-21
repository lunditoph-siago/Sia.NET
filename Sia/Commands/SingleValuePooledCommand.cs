namespace Sia;

public abstract record SingleValuePooledCommand<TCommand, T> : PooledCommand<TCommand>
    where TCommand : ICommand, new()
{
    public T? Value { get; set; }

    public static TCommand Create(T value)
    {
        var cmd = Create();
        (cmd as SingleValuePooledCommand<TCommand, T>)!.Value = value;
        return cmd;
    }
}