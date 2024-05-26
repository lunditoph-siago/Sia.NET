namespace Sia.Reactors;

using System.Collections.Immutable;

public partial record struct Node<TTag>
{
    public readonly record struct OnIsEnabledChanged(bool Enabled) : IEvent;
    public readonly record struct ChildAdded(Entity Entity) : IEvent;
    public readonly record struct ChildRemoved(Entity Entity) : IEvent;

    public readonly bool IsEnabled => _enabled;

    [Sia]
    public bool IsSelfEnabled { get; set; } = true;

    [Sia]
    public Entity? Parent {
        readonly get => _parent;
        set {
            _prevParent = _parent;
            _parent = value;
        }
    }

    public readonly IReadOnlySet<Entity> Children =>
        _children ?? (IReadOnlySet<Entity>)ImmutableHashSet<Entity>.Empty;

    internal bool _enabled = true;

    internal Entity? _parent;
    internal Entity? _prevParent;
    internal HashSet<Entity>? _children;

    public Node(Entity? parent)
    {
        _parent = parent;
    }
}