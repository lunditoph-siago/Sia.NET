namespace Sia.Reactors;

using System.Collections.Immutable;

public partial record struct Node<TTag>
{
    public readonly record struct OnIsEnabledChanged(bool Enabled) : IEvent;
    public readonly record struct ChildAdded(EntityRef Entity) : IEvent;
    public readonly record struct ChildRemoved(EntityRef Entity) : IEvent;

    public readonly bool IsEnabled => _enabled;

    [Sia]
    public bool IsSelfEnabled { get; set; } = true;

    [Sia]
    public Identity? Parent {
        readonly get => _parent;
        set {
            _prevParent = _parent;
            _parent = value;
        }
    }

    public readonly IReadOnlySet<Identity> Children =>
        _children ?? (IReadOnlySet<Identity>)ImmutableHashSet<Identity>.Empty;

    internal bool _enabled = true;

    internal Identity? _parent;
    internal Identity? _prevParent;
    internal HashSet<Identity>? _children;

    public Node(Identity? parent)
    {
        _parent = parent;
    }
}