namespace Sia;

public interface ICommand<TTarget> : IEvent
{
    void Execute(in TTarget target);
}

public interface ICommand : ICommand<EntityRef>
{
}