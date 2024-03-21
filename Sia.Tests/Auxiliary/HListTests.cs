using System.Numerics;

namespace Sia.Tests.Auxiliary;

public class HListTests
{
    public class MockHandler : IGenericHandler
    {
        public List<object> HandledValues { get; } = new();

        public void Handle<T>(in T value)
        {
            HandledValues.Add(value);
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

    [Fact]
    public void PolyList_HandleHead_Test()
    {
        // Arrange
        const int headValue = 1;
        var list = HList.Create(headValue);
        var mockHandler = new MockHandler();

        // Act
        list.HandleHead(mockHandler);

        // Assert
        Assert.All(mockHandler.HandledValues, data => Assert.Equal(headValue, data));
    }

    [Fact]
    public void PolyList_ConsTwoPolyLists_Test()
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
        Assert.All(mockHandler.HandledValues, (data, index) =>
        {
            switch (index)
            {
                case 0:
                    Assert.Equal(HList.Create("Mock", 1.0f, new Vector3(1, 2, 3), true), data);
                    break;
                case 1:
                    Assert.Equal(HList.Create("Mock"), data);
                    break;
                case 2:
                    Assert.Equal(HList.Create(new Vector3(1, 2, 3)), data);
                    break;
            }
        });
    }
}