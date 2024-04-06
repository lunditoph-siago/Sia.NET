namespace Sia.Tests.Auxiliary;

public class TypeUnionTests
{
    [Fact]
    public void TypeUnion_Creation_Test()
    {
        Assert.Multiple(
            () => {
                // Arrange
                var union = new TypeUnion<int>();

                // Assert
                Assert.Single(union.Types);
                Assert.Equal(typeof(int), union.Types[0]);
            },
            () => {
                // Arrange
                var union = new TypeUnion<int, string>();

                // Assert
                Assert.Equal(2, union.Types.Length);
                Assert.Contains(typeof(int), union.Types);
                Assert.Contains(typeof(string), union.Types);
            });
    }

    [Fact]
    public void TypeUnion_Equality_Test()
    {
        Assert.Multiple(
            () => {
                // Arrange
                var union1 = new TypeUnion<int, string, uint>();
                var union2 = new TypeUnion<int, string, uint>();

                // Assert
                Assert.Equal(union1.GetHashCode(), union2.GetHashCode());
            },
            () => {
                // Arrange
                var union1 = new TypeUnion<int, string, uint>();
                var union2 = new TypeUnion<string, uint, int>();

                // Assert
                Assert.Equal(union1.GetHashCode(), union2.GetHashCode());
            },
            () => {
                // Arrange
                var union1 = new TypeUnion<int, string, uint>();
                var union2 = new TypeUnion<long, long, long>();

                // Assert
                Assert.NotEqual(union1.GetHashCode(), union2.GetHashCode());
            });
    }

    [Fact]
    public void TypeUnion_Match_Test()
    {
        Assert.Multiple(
            () => {
                // Arrange
                var dict = new Dictionary<ITypeUnion, int>
                {
                    { new TypeUnion<int, string>(), 1 }
                };

                // Assert
                Assert.Equal(1, dict[new TypeUnion<string, int>()]); 
            },
            () => {
                // Arrange
                var dict = new Dictionary<ITypeUnion, int>
                {
                    { new TypeUnion<int, string>(), 1 },
                    { new TypeUnion<string, string>(), 2 }
                };

                // Assert
                Assert.Equal(1, dict[new TypeUnion<string, int>()]);
                Assert.Equal(2, dict[new TypeUnion<string, string, string>()]);
            });
    }
}