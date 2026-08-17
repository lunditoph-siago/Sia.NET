namespace Sia;

public sealed class WorldDispatcher : Dispatcher<IEvent>
{
    public World World { get; }

    internal WorldDispatcher(World world)
    {
        World = world;
    }

    public new void Send<UEvent>(Entity target, in UEvent e)
        where UEvent : IEvent
    {
        World.EnsureOwnerThread();
        base.Send(target, e);
    }
}