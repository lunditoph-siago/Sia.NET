namespace Sia.Reactive;

internal readonly record struct CellIdentity(long Value)
{
    private static long s_next;

    public static CellIdentity Create()
        => new(Interlocked.Increment(ref s_next));
}

public struct Cell
{
    internal CellIdentity Identity;
    public Entity? Parent;
    public int Depth;
    public int SlotInParent;
    public Entity?[] Slots;
    public Expander Expander;
    public ScheduleRegistry? Schedule;
    public ContextScope? Scope;
    public StateCells? States;
    public bool InDirty;
}

public struct PrevTree<TTree>
    where TTree : struct, ITerm<TTree>
{
    public TTree Value;
    public bool Mounted;
}
