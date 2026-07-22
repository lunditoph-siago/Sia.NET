namespace Sia.Reactive;

public readonly record struct NodeIdentity(long Value);

public struct CellSlot(Entity entity = default)
{
    private Entity _entity = entity.IsValid ? entity : default;

    public Entity Entity {
        readonly get => _entity.IsValid ? _entity : default;
        set => _entity = value.IsValid ? value : default;
    }
}

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
    internal Entity Output { get; init; }
    public StateCells? States;
    internal bool HookLayoutInitialized;
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
