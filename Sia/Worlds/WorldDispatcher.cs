namespace Sia;

public class WorldDispatcher<TTarget> : Dispatcher<TTarget>
    where TTarget : notnull
{
    public World<TTarget> World { get; }

    internal WorldDispatcher(World<TTarget> world)
    {
        World = world;
    }
}