using System.Numerics;

namespace Sia.Tests.Auxiliary;

public class PolyListTests
{
    public class MockHandler : IGenericHandler
    {
        public List<object> HandledValues { get; } = new();

        public void Handle<T>(in T value)
        {
            HandledValues.Add(value);
        }
    }

    public class MockGenericHandler : IGenericHandler<IPolyList>
    {
        public List<IPolyList> HandledValues { get; } = new();

        public void Handle<T>(in T value) where T : IPolyList
        {
            HandledValues.Add(value);
        }
    }

    [Fact]
    public void PolyList_HandleHead_Test()
    {
        // Arrange
        const int headValue = 1;
        var list = PolyList.Create(headValue);
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
        var list1 = PolyList.Cons("Mock", PolyList.Create(1.0f));
        var list2 = PolyList.Cons(new Vector3(1, 2, 3), PolyList.Create(true));
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
                    Assert.Equal(PolyList.Cons("Mock", PolyList.Cons(1.0f, PolyList.Cons(new Vector3(1, 2, 3), PolyList.Create(true)))), data);
                    break;
                case 1:
                    Assert.Equal(PolyList.Cons("Mock", EmptyPolyList.Default), data);
                    break;
                case 2:
                    Assert.Equal(PolyList.Cons(new Vector3(1, 2, 3), EmptyPolyList.Default), data);
                    break;
            }
        });
    }
}