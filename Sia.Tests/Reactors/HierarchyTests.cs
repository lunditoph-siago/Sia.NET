namespace Sia.Tests.Reactors;

[TestCaseOrderer("Sia.Tests.PriorityOrderer", "Sia.Tests")]
public class HierarchyTests : IDisposable
{
    public readonly record struct Name(string Value);
    
    public sealed class TestTag;

    public static List<object[]> HierarchyTestData =>
    [
        [new ValueTuple<string, int>[] { new("test1", -1), new("test2", 0), new("test3", 0),  new("test4", 2) }]
    ];

    public static readonly List<EntityRef>? EntityRefs = [];

    public static Hierarchy<TestTag>? Hierarchy;

    public static World? World;

    public HierarchyTests()
    {
        World = new World();
        Context<World>.Current = World;

        Hierarchy = World.AcquireAddon<Hierarchy<TestTag>>();
    }

    [Theory, Priority(0)]
    [MemberData(nameof(HierarchyTestData))]
    public void Hierarchy_Setup_Test((string, int)[] data)
    {
        // Arrange
        foreach (var (name, index) in data) {
            var node = index >= 0 ? new Node<TestTag>(EntityRefs?[index]) : new Node<TestTag>();
            var entityRef = World!.CreateInArrayHost(HList.Create(node, new Name(name)));
            EntityRefs?.Add(entityRef);
        }

        // Act
        var actualChildren = EntityRefs?.First().Get<Node<TestTag>>().Children
            .Select(child => child.Get<Name>().Value);
        var expectedChildren = data.Where(value => value.Item2 == 0).Select(value => value.Item1);

        // Assert
        Assert.Equal(expectedChildren, actualChildren);
    }

    [Fact, Priority(1)]
    public void Hierarchy_Modify_Test()
    {
        // Act
        World?.Modify(EntityRefs![3], new Node<TestTag>.SetParent(EntityRefs[0]));

        // Assert
        Assert.InRange(EntityRefs!.First().Get<Node<TestTag>>().Children
            .Select(child => child.Get<Name>().Value).Count(),
            0,
            int.MaxValue);
    }

    public void Dispose() => World?.Dispose();
}