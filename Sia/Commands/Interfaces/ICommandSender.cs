namespace Sia;

public interface ICommandSender<TCommand, TTarget>
    where TCommand : ICommand
{
    void Send(TTarget target, TCommand command);
}

public interface ICommandSender<TCommand> : ICommandSender<TCommand, EntityRef>
    where TCommand : ICommand
{
}