namespace Sia.Reactive;

public readonly record struct NodeIdentity(long Value)
{
    private static long s_next;

    public static NodeIdentity Create()
        => new(Interlocked.Increment(ref s_next));
}

public struct CellSlot(Entity? entity = null, NodeIdentity identity = default)
{
    public Entity? Entity = entity;
    public NodeIdentity Identity = identity;

    public void Set(Reconciler reconciler, Entity entity)
        => (Entity, Identity) = (entity, reconciler.GetIdentity(entity));
}

public readonly record struct ReactiveNode(NodeIdentity Identity);

public struct Cell
{
    public NodeIdentity Identity { get; init; }
    public Entity? Parent { get; init; }
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
