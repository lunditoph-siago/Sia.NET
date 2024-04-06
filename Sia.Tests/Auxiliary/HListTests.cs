using System.Numerics;

namespace Sia.Tests.Auxiliary;

public class HListTests
{
    public class MockHandler : IGenericHandler
    {
        public List<object> HandledValues { get; } = [];

        public void Handle<T>(in T value)
        {
            HandledValues.Add(value!);
        }
    }

    public class MockGenericHandler : IGenericHandler<IHList>
    {
        public List<IHList> HandledValues { get; } = new();

        public void Handle<T>(in T value) where T : IHList
        {
            HandledValues.Add(value);
        }
    }

    [Theory]
    [InlineData(1)]
    public void HList_HandleHead_Test(int value)
    {
        // Arrange
        var list = HList.Create(value);
        var mockHandler = new MockHandler();

        // Act
        list.HandleHead(mockHandler);

        // Assert
        Assert.All(mockHandler.HandledValues, data => Assert.Equal(value, data));
    }

    [Fact]
    public void HList_ConsTwoHLists_Test()
    {
        // Arrange
        var list1 = HList.Create("Mock", 1.0f);
        var list2 = HList.Create(new Vector3(1, 2, 3), true);
        var mockHandler = new MockGenericHandler();

        // Act
        list1.Concat(list2, mockHandler);
        list1.Remove(1.0f, mockHandler);
        list2.Remove(TypeProxy<bool>.Default, mockHandler);

        // Assert
        Assert.Equal(HList.Create("Mock", 1.0f, new Vector3(1, 2, 3), true), mockHandler.HandledValues[0]);
        Assert.Equal(HList.Create("Mock"), mockHandler.HandledValues[1]);
        Assert.Equal(HList.Create(new Vector3(1, 2, 3)), mockHandler.HandledValues[2]);
    }
}