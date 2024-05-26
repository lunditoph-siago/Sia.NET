namespace Sia;

public interface ICommand : IEvent
{
    void Execute(World world, Entity target);
}

public interface ICommand<TComponent> : ICommand
{
    void Execute(World world, Entity target, ref TComponent component);
}