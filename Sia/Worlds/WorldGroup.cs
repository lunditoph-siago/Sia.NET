namespace Sia;

public class WorldGroup<T> : Group<T>
    where T : notnull
{
    public World<T> World { get; }
    public World<T>.GroupPredicate? Predicate { get; }

    internal int Index { get; set; }

    internal WorldGroup(World<T> world, World<T>.GroupPredicate? predicate)
    {
        World = world;
        Predicate = predicate;
    }
}