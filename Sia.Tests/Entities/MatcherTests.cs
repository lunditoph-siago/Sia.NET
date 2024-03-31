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
}