namespace Sia;

public class WorldDispatcher<T> : Dispatcher<T>
    where T : notnull
{
    public World<T> World { get; }

    internal WorldDispatcher(World<T> world)
    {
        World = world;
    }
}