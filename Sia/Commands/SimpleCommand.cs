namespace Sia;

public abstract class SimpleCommand<TCommand> : Command<TCommand>
    where TCommand : SimpleCommand<TCommand>, new()
{
    public static TCommand Create() => CreateRaw();
}