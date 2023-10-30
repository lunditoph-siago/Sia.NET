namespace Sia;

public interface ICommand : IEvent
{
    void Execute(World world, in EntityRef target);
}

public interface ICommand<T> : IEvent
{
    void Execute(World world, ref T component);
}