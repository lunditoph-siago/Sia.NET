namespace Sia;

public class WorldGroup<T> : Group<T>
    where T : notnull
{
    public World<T> World { get; }
    public Predicate<T>? Predicate { get; }

    internal int Index { get; set; }

    internal WorldGroup(World<T> world, Predicate<T>? predicate)
    {
        World = world;
        Predicate = predicate;
    }
}