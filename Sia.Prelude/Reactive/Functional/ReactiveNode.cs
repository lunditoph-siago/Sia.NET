namespace Sia.Reactive;

public readonly record struct ReactiveNode<TTerm>(TTerm Term)
    where TTerm : struct, ITerm<TTerm>
{
    public static implicit operator ReactiveNode(ReactiveNode<TTerm> node)
        => ReactiveNode.Erase(node);
}

public readonly struct ReactiveNode : IEquatable<ReactiveNode>
{
    private readonly IBox? _box;

    private IBox CurrentBox => _box ?? Box<UnitTerm>.Empty;

    private ReactiveNode(IBox box)
        => _box = box ?? throw new ArgumentNullException(nameof(box));

    public static ReactiveNode Erase<TTerm>(
        scoped in ReactiveNode<TTerm> node)
        where TTerm : struct, ITerm<TTerm>
        => new(new Box<TTerm>(node.Term));

    internal void Mount(ref GraphContext context)
        => CurrentBox.Mount(ref context);

    internal void Reconcile(
        scoped in ReactiveNode previous,
        ref GraphContext context)
        => CurrentBox.Reconcile(previous.CurrentBox, ref context);

    internal bool HasSameShape(scoped in ReactiveNode other)
        => CurrentBox.GetType() == other.CurrentBox.GetType();

    public bool Equals(ReactiveNode other)
        => CurrentBox.EqualsNode(other.CurrentBox);

    public override bool Equals(object? obj)
        => obj is ReactiveNode other && Equals(other);

    public override int GetHashCode()
        => CurrentBox.GetNodeHashCode();

    public static bool operator ==(ReactiveNode left, ReactiveNode right)
        => left.Equals(right);

    public static bool operator !=(ReactiveNode left, ReactiveNode right)
        => !left.Equals(right);

    private interface IBox
    {
        void Mount(ref GraphContext context);
        void Reconcile(IBox previous, ref GraphContext context);
        bool EqualsNode(IBox other);
        int GetNodeHashCode();
    }

    private sealed class Box<TTerm>(TTerm term) : IBox
        where TTerm : struct, ITerm<TTerm>
    {
        public static readonly Box<UnitTerm> Empty = new(default);

        private readonly TTerm _term = term;

        public void Mount(ref GraphContext context)
        {
            var node = context.Reconciler.MountSub(
                new ReactiveNodeSpec<TTerm>(_term),
                context.Cell,
                context.Depth + 1,
                context.NextSlotIndex,
                context.Schedule,
                context.Scope,
                context.Output,
                context.MessageOwner);
            context.SetSlot(node);
        }

        public void Reconcile(IBox previous, ref GraphContext context)
        {
            if (previous is not Box<TTerm>) {
                throw new InvalidOperationException(
                    "A reactive node was reconciled against an incompatible root shape.");
            }

            var node = context.PeekSlot();
            if (node is not { IsValid: true }
                || !node.ContainsUnchecked<ReactiveNodeSpec<TTerm>>()) {
                Mount(ref context);
                return;
            }

            var next = new ReactiveNodeSpec<TTerm>(_term);
            if (!EqualityComparer<ReactiveNodeSpec<TTerm>>.Default.Equals(
                    node.GetUnchecked<ReactiveNodeSpec<TTerm>>(), next)) {
                context.Reconciler.UpdateMount(node, next);
            }
            context.Advance();
        }

        public bool EqualsNode(IBox other)
            => other is Box<TTerm> node
                && EqualityComparer<TTerm>.Default.Equals(_term, node._term);

        public int GetNodeHashCode()
            => HashCode.Combine(typeof(TTerm), _term);
    }
}

public readonly record struct ReactiveNodeSpec<TTerm>(TTerm Tree)
    : ISpec<ReactiveNodeSpec<TTerm>, Unit, TTerm>
    where TTerm : struct, ITerm<TTerm>
{
    public static TTerm Expand(
        in ReactiveNodeSpec<TTerm> props,
        in Unit state,
        in ExpandContext context)
        => props.Tree;
}

internal readonly record struct OpaqueTerm(ReactiveNode Node)
    : ITerm<OpaqueTerm>
{
    public static int SlotCount => 1;

    public static void Mount(in OpaqueTerm self, ref GraphContext context)
        => self.Node.Mount(ref context);

    public static void Reconcile(
        in OpaqueTerm previous,
        in OpaqueTerm next,
        ref GraphContext context)
    {
        if (previous.Node.HasSameShape(next.Node)) {
            next.Node.Reconcile(previous.Node, ref context);
            return;
        }

        var slot = context.NextSlotIndex;
        context.DestroyRange(1);
        context.RewindTo(slot);
        next.Node.Mount(ref context);
    }
}
