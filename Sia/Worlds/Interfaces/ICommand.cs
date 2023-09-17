namespace Sia;

public interface ICommand : IEvent
{
    void Execute(World world, in EntityRef target);
}