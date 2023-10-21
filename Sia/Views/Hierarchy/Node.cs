namespace Sia;

using System.Collections;

public record struct Node<TTag>(EntityRef? Parent)
{
    public readonly record struct ChildAdded(EntityRef Entity) : IEvent;
    public readonly record struct ChildRemoved(EntityRef Entity) : IEvent;

    public readonly IReadOnlySet<EntityRef> Children => _children ?? s_emptySet;

    internal HashSet<EntityRef> _children;
    internal EntityRef? PreviousParent { get; private set; }

    private static readonly HashSet<EntityRef> s_emptySet = new();

    public readonly record struct SetParent(EntityRef? Value) : IParallelCommand, IReconstructableCommand<SetParent>
    {
        public static SetParent ReconstructFromCurrentState(in EntityRef entity)
            => new(entity.Get<Node<TTag>>().Parent);

        public void Execute(World world, in EntityRef target)
            => ExecuteOnParallel(target);

        public void ExecuteOnParallel(in EntityRef target)
        {
            if (Value != null) {
                EntityUtility.CheckComponent<Node<TTag>>(Value.Value);
            }
            ref var node = ref target.Get<Node<TTag>>();
            node.PreviousParent = node.Parent;
            node.Parent = Value;
        }
    }
}