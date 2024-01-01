namespace Sia;

public interface ICommand : IEvent
{
    void Execute(World world, in EntityRef target);
}

public interface ICommand<TComponent> : ICommand
{
    void Execute(World world, in EntityRef target, ref TComponent component);
}