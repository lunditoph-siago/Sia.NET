namespace Sia.Tests.Entities;

public class MatcherTests
{
    public static List<object[]> MatcherTestData =>
    [
        [Matchers.Of<int>(), new TypeUnion<int>().ToMatcher(), true],
        [Matchers.Of<int>(), new TypeUnion<long>().ToMatcher(), false],
        [Matchers.Of<int, float>().With(new TypeUnion<long>()), new TypeUnion<int, float, long>().ToMatcher(), true]
    ];

    [Theory]
    [MemberData(nameof(MatcherTestData))]
    public void Matcher_Equality_Test(IEntityMatcher left, IEntityMatcher right, bool result)
    {
        Assert.Equal(result, left.Equals(right));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Query_WithoutCacheOwnsItsHostSubscription(bool matchAny)
    {
        using var world = new World();
        var matcher = matchAny ? Matchers.Any : Matchers.Of<int>();
        using var shared = world.Query(matcher);
        using var owned = Assert.IsType<World.EntityQuery>(world.Query(matcher, cache: false));
        using var other = world.Query(matcher, cache: false);

        Assert.Same(shared, world.Query(matcher, cache: true));
        Assert.NotSame(owned, other);
        Assert.DoesNotContain(owned, world.Queries.Values);
        world.Create(HList.From(1));
        Assert.Equal(1, owned.Count);

        owned.Dispose();
        world.Create(HList.From(2, true));
        Assert.Empty(owned.Hosts);
        Assert.Equal(2, other.Count);
        Assert.Equal(2, shared.Count);
    }
}
