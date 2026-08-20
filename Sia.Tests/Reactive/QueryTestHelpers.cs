namespace Sia.Tests.Reactive;

public sealed class QueryTestHelpers
{
    public Entity[] FindAll<T>(World world)
        => [.. world.Query(Matchers.Of<T>()).Hosts.SelectMany(static host => host)];

    public Entity FindSingle<T>(World world)
        => Assert.Single(world.Query(Matchers.Of<T>()).Hosts.SelectMany(static host => host));

    public Dictionary<TKey, Entity> FindAllByKey<TComponent, TKey>(
        World world, Func<TComponent, TKey> keySelector)
        where TKey : notnull
        => FindAll<TComponent>(world).ToDictionary(entity => keySelector(entity.Get<TComponent>()));
}
