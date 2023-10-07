namespace Sia;

public sealed class WorldDispatcher : Dispatcher<EntityRef, IEvent>
{
    public World World { get; }

    internal WorldDispatcher(World world)
    {
        World = world;
    }
}