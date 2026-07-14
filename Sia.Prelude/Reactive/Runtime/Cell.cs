namespace Sia.Reactive;

internal readonly record struct NodeIdentity(long Value)
{
    private static long s_next;

    public static NodeIdentity Create()
        => new(Interlocked.Increment(ref s_next));
}

internal struct CellSlot
{
    public Entity? Entity;
    public NodeIdentity Identity;

    public void Set(Reconciler reconciler, Entity entity)
        => (Entity, Identity) = (entity, reconciler.GetIdentity(entity));
}

internal struct ReactiveNode
{
    public NodeIdentity Identity;
}

public struct Cell
{
    internal NodeIdentity Identity;
    public Entity? Parent;
    public int Depth;
    public int SlotInParent;
    internal CellSlot[] Slots;
    public Expander Expander;
    public ScheduleRegistry? Schedule;
    public ContextScope? Scope;
    public StateCells? States;
    public bool InDirty;
    internal bool IsDestroying;
}

public struct PrevTree<TTree>
    where TTree : struct, ITerm<TTree>
{
    public TTree Value;
    public bool Mounted;
}
