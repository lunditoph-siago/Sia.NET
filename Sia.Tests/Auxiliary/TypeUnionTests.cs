namespace Sia.Tests.Auxiliary;

public class TypeUnionTests
{
    public static List<object[]> TypeUnionTestData =>
    [
        [new TypeUnion<bool>()],
        [new TypeUnion<bool, byte>()],
        [new TypeUnion<bool, byte>()],
        [new TypeUnion<bool, byte, short>()],
        [new TypeUnion<bool, byte, short>()],
        [new TypeUnion<bool, byte, short, int>()],
        [new TypeUnion<bool, byte, short, int>()],
        [new TypeUnion<bool, byte, short, int, long>()],
        [new TypeUnion<bool, byte, short, int, long>()],
        [new TypeUnion<bool, byte, short, int, long, float>()],
        [new TypeUnion<bool, byte, short, int, long, float, double>()],
        [new TypeUnion<bool, byte, short, int, long, float, double, decimal>()],
        [new TypeUnion<bool, byte, short, int, long, float, double, decimal, char>()],
        [new TypeUnion<bool, byte, short, int, long, float, double, decimal, char, string>()],
        [new TypeUnion<bool, byte, short, int, long, float, double, decimal, char, string, object>()],
        [new TypeUnion<bool, byte, short, int, long, float, double, decimal, char, string, object, dynamic>()],
        [new TypeUnion<bool, byte, short, int, long, float, double, decimal, char, string, object, dynamic, Action<float>>()],
        [new TypeUnion<bool, byte, short, int, long, float, double, decimal, char, string, object, dynamic, Action<float>, TypeCode>()],
        [new TypeUnion<bool, byte, short, int, long, float, double, decimal, char, string, object, dynamic, Action<float>, TypeCode, Guid>()],
        [new TypeUnion<bool, byte, short, int, long, float, double, decimal, char, string, object, dynamic, Action<float>, TypeCode, Guid, Random>()]
    ];

    [Theory]
    [MemberData(nameof(TypeUnionTestData))]
    public void TypeUnion_Creation_Test(ITypeUnion typeUnion)
    {
        foreach (var type in typeUnion.Types) {
            Assert.Contains(type, typeUnion.Types);
        }
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