namespace Sia;

using System.Collections;

public record struct Node(EntityRef? Parent) : IEnumerable<EntityRef>
{
    public readonly record struct ChildAdded(EntityRef Entity) : IEvent;
    public readonly record struct ChildRemoved(EntityRef Entity) : IEvent;

    public readonly IEnumerable<EntityRef> Children =>
        _children ?? Enumerable.Empty<EntityRef>();

    internal HashSet<EntityRef>? _children;

    internal EntityRef? PreviousParent { get; private set; }

    public readonly record struct SetParent(EntityRef? Value) : IParallelCommand
    {
        public void Execute(World world, in EntityRef target)
            => ExecuteOnParallel(target);

        public void ExecuteOnParallel(in EntityRef target)
        {
            if (Value != null) {
                EntityUtility.CheckComponent<Node>(Value.Value);
            }
            ref var node = ref target.Get<Node>();
            node.PreviousParent = node.Parent;
            node.Parent = Value;
        }
    }

    public readonly IEnumerator<EntityRef> GetEnumerator()
        => Children.GetEnumerator();

    readonly IEnumerator IEnumerable.GetEnumerator()
        => Children.GetEnumerator();
}