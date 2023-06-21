namespace Sia;

public record BatchedCommand<TCommand, TTarget> : PooledCommand<BatchedCommand<TCommand, TTarget>>
    where TCommand : ICommand
    where TTarget : notnull
{
    public List<(TTarget, TCommand)> Commands { get; } = new();

    public override void Dispose()
    {
        Commands.Clear();
        base.Dispose();
    }
}

public record BatchedCommand<TCommand> : BatchedCommand<TCommand, EntityRef>
    where TCommand : ICommand
{
}

public record BatchedCommand : BatchedCommand<ICommand>
{
}
