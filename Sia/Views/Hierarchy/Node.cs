namespace Sia;

public record struct Node<TTag>(EntityRef? Parent)
{
    public readonly record struct ChildAdded(EntityRef Entity) : IEvent;
    public readonly record struct ChildRemoved(EntityRef Entity) : IEvent;

    public readonly IReadOnlySet<EntityRef> Children => _children ?? s_emptySet;

    internal HashSet<EntityRef> _children;
    internal EntityRef? _prevParent;

    private static readonly HashSet<EntityRef> s_emptySet = new();

    public readonly record struct SetParent(EntityRef? Value) : IParallelCommand<Node<TTag>>, IReconstructableCommand<SetParent>
    {
        public static SetParent ReconstructFromCurrentState(in EntityRef entity)
            => new(entity.Get<Node<TTag>>().Parent);

        public void Execute(World world, in EntityRef target)
            => ExecuteOnParallel(target);

        public void Execute(World world, in EntityRef target, ref Node<TTag> component)
            => ExecuteOnParallel(ref component);

        public void ExecuteOnParallel(in EntityRef target)
            => ExecuteOnParallel(ref target.Get<Node<TTag>>());

        public void ExecuteOnParallel(ref Node<TTag> node)
        {
            if (Value != null) {
                EntityUtility.CheckComponent<Node<TTag>>(Value.Value);
            }
            node._prevParent = node.Parent;
            node.Parent = Value;
        }
    }
}