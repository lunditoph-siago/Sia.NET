namespace Sia.Tests.Reactors;

using Sia.Reactors;

[TestCaseOrderer("Sia.Tests.PriorityOrderer", "Sia.Tests")]
public class HierarchyTests(HierarchyTests.HierarchyContext context) : IClassFixture<HierarchyTests.HierarchyContext>
{
    public class HierarchyContext : IDisposable
    {
        public readonly record struct Name(string Value);

        public sealed class TestTag;

        public List<Entity> Entities = [];

        public Hierarchy<TestTag> Hierarchy;

        public World World;

        public HierarchyContext()
        {
            World = new World();
            Context<World>.Current = World;

            Hierarchy = World.AcquireAddon<Hierarchy<TestTag>>();
        }

        public void Dispose() => World.Dispose();
    }

    public static List<object[]> HierarchyTestData => [
        [new ValueTuple<string, int>[] { new("test1", -1), new("test2", 0), new("test3", 0),  new("test4", 2) }]
    ];

    [Theory, Priority(0)]
    [MemberData(nameof(HierarchyTestData))]
    public void Hierarchy_Setup_Test((string, int)[] hierarchies)
    {
        // Arrange
        foreach (var (name, index) in hierarchies) {
            var node = index >= 0
                ? new Node<HierarchyContext.TestTag>(context.Entities[index])
                : new Node<HierarchyContext.TestTag>();
            var entityRef = context.World.Create(HList.From(node, new HierarchyContext.Name(name)));
            context.Entities.Add(entityRef);
        }

        // Act
        var actualChildren = context.Entities.First().Get<Node<HierarchyContext.TestTag>>().Children
            .Select(child => child.Get<HierarchyContext.Name>().Value);
        var expectedChildren = hierarchies.Where(value => value.Item2 == 0).Select(value => value.Item1);

        // Assert
        Assert.Equal(expectedChildren, actualChildren);
    }

    [Theory, Priority(1)]
    [InlineData(3)]
    public void Hierarchy_Execute_Test(int target)
    {
        // Act
        context.World.Execute(context.Entities[target],
            new Node<HierarchyContext.TestTag>.SetParent(context.Entities[0]));
    
        // Assert
        Assert.True(
            context.Entities[0].Get<Node<HierarchyContext.TestTag>>().Children
                .Select(child => child.Get<HierarchyContext.Name>().Value)
                .Any());
    }
}