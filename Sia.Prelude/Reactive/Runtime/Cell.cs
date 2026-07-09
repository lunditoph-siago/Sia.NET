namespace Sia.Reactive;

public struct Cell
{
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
