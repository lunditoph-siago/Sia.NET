namespace Sia;

public interface ICommand : IEvent
{
    void Execute(World world, in EntityRef target);
}

public interface ICommand<T> : ICommand
{
    void Execute(World world, in EntityRef target, ref T component);
}