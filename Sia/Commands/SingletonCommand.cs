namespace Sia;

public abstract record SingletonCommand<TCommand> : Command
    where TCommand : ICommand, new()
{
    public static TCommand Instance { get; } = new();

    protected SingletonCommand() {}
}