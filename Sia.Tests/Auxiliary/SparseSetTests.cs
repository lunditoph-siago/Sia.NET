namespace Sia.Tests.Auxiliary;

public class SparseSetTests
{
    [Fact]
    public void SparseSet_Indexer_GetSet_Test()
    {
        // Arrange
        var sparseSet = new SparseSet<string> { { 2, "World" } };

        // Act
        var value = sparseSet[2];

        // Assert
        Assert.Equal("World", value);
        Assert.Throws<KeyNotFoundException>(() => sparseSet[1]);
    }

    [Fact]
    public void SparseSet_Clear_Test()
    {
        // Arrange
        var sparseSet = new SparseSet<string> { { 1, "Item1" }, { 2, "Item2" } };

        // Act
        sparseSet.Clear();

        // Assert
        Assert.Empty(sparseSet);
    }

    [Fact]
    public void SparseSet_Contains_Test()
    {
        // Arrange
        var sparseSet = new SparseSet<string> { { 10, "Item10" } };

        // Act
        var existed = sparseSet.Contains(new KeyValuePair<int, string>(10, "Item10"));
        var none = sparseSet.Contains(new KeyValuePair<int, string>(10, "FakeItem10"));

        // Assert
        Assert.True(existed);
        Assert.False(none);
    }

    [Fact]
    public void SparseSet_TryGetValue_Test()
    {
        // Arrange
        var sparseSet = new SparseSet<string>();
        const int testKey = 6;
        const string testValue = "Sia";
        sparseSet.Add(testKey, testValue);

        // Act
        var result = sparseSet.TryGetValue(testKey, out var value);

        // Assert
        Assert.True(result);
        Assert.Equal(testValue, value);
    }

    [Fact]
    public void SparseSet_RemoveByKey_Test()
    {
        // Arrange
        var sparseSet = new SparseSet<int> { { 1, 10 } };

        // Act
        var action1 = sparseSet.Remove(1, out var removedValue);
        var action2 = sparseSet.Remove(1, out _);

        // Assert
        Assert.True(action1);
        Assert.False(action2);
        Assert.Equal(10, removedValue);
        Assert.Empty(sparseSet);
    }

    [Fact]
    public void SparseSet_RemoveKeyValuePair_Test()
    {
        // Arrange
        var sparseSet = new SparseSet<string> { { 1, "Item1" } };
        var kvpToRemove = new KeyValuePair<int, string>(1, "Item1");

        // Act
        var action1 = sparseSet.Remove(kvpToRemove);
        var action2 = sparseSet.Remove(kvpToRemove);

        // Assert
        Assert.True(action1);
        Assert.False(action2);
        Assert.Empty(sparseSet);
    }

    [Fact]
    public void SparseSet_Enumerator_Test()
    {
        // Arrange
        var sparseSet = new SparseSet<string>();
        var items = new List<KeyValuePair<int, string>> { new(3, "Item3"), new(2, "Item2"), new(1, "Item1") };
        foreach (var item in items) sparseSet.Add(item.Key, item.Value);

        // Act
        var resultOrder = sparseSet.Select(item => item.Value).ToList();

        // Assert
        Assert.Equal(["Item3", "Item2", "Item1"], resultOrder);
    }
}