namespace Sia.Tests.Auxiliary;

public class TypeUnionTests
{
    [Fact]
    public void TypeUnion_Ordering_Test()
    {
        // Arrange
        var u1 = new TypeUnion<int, string, uint>();
        var u2 = new TypeUnion<string, uint, int>();

        // Assert
        Assert.Equal(u1.GetHashCode(), u2.GetHashCode());
    }

    [Fact]
    public void TypeUnion_SingleMatch_Test()
    {
        // Arrange
        var dict = new Dictionary<ITypeUnion, int>
        {
            { new TypeUnion<int, string>(), 1 }
        };

        // Assert
        Assert.Equal(1, dict[new TypeUnion<string, int>()]);
    }

    [Fact]
    public void TypeUnion_MultiMatch_Test()
    {
        // Arrange
        var dict = new Dictionary<ITypeUnion, int>
        {
            { new TypeUnion<int, string>(), 1 },
            { new TypeUnion<string, string>(), 2 }
        };

        // Assert
        Assert.Equal(1, dict[new TypeUnion<string, int>()]);
        Assert.Equal(2, dict[new TypeUnion<string, string, string>()]);
    }
}