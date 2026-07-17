namespace Sia.Reactive;

public readonly record struct NodeIdentity(long Value)
{
    private static long s_next;

    public static NodeIdentity Create()
        => new(Interlocked.Increment(ref s_next));
}

public struct CellSlot(Entity entity = default)
{
    private Entity _entity = entity.IsValid ? entity : default;

    public Entity Entity {
        readonly get => _entity.IsValid ? _entity : default;
        set => _entity = value.IsValid ? value : default;
    }

    public readonly NodeIdentity Identity {
        get {
            var entity = _entity;
            if (!entity.IsValid) {
                return default;
            }
            if (entity.ContainsUnchecked<Cell>()) {
                return entity.GetUnchecked<Cell>().Identity;
            }
            return entity.ContainsUnchecked<ReactiveNode>()
                ? entity.GetUnchecked<ReactiveNode>().Identity
                : default;
        }
    }

    public CellSlot(Entity entity, NodeIdentity identity)
        : this(entity)
    {
        if (Identity != identity) {
            _entity = default;
        }
    }

    public void Set(Entity entity)
        => _entity = entity.IsValid ? entity : default;
}

public readonly record struct ReactiveNode(NodeIdentity Identity);

public struct Cell
{
    private EntityReference _parent;

    public NodeIdentity Identity { get; init; }
    public Entity? Parent {
        readonly get => _parent.TryGet(out var parent) ? parent : null;
        init => _parent = value is { IsValid: true } parent ? new(parent) : default;
    }
    internal readonly Entity ParentEntity => _parent.GetOrDefault();
    public int Depth { get; init; }
    public int SlotInParent { get; init; }
    public CellSlot[] Slots { get; init; }
    public Expander Expander { get; init; }
    public ScheduleRegistry? Schedule { get; init; }
    public ContextScope? Scope { get; init; }
    public StateCells? States;
    public List<ContextScope>? ContextDependencies;
    public List<ContextScope>? PendingContextDependencies;
    public bool InDirty;
    public bool IsDestroying;
}

public struct PrevTree<TTree>
    where TTree : struct, ITerm<TTree>
{
    public TTree Value;
    public bool Mounted;
}
