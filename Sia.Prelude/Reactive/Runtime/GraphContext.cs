namespace Sia.Reactive;

using System.Runtime.CompilerServices;

public ref struct GraphContext
{
    public readonly Reconciler Reconciler;
    public readonly World World;
    public readonly Entity Cell;
    public readonly int Depth;

    public ScheduleRegistry? Schedule;
    public ContextScope? Scope;

    private readonly CellSlot[] _slots;
    private int _cursor;

    internal GraphContext(
        Reconciler reconciler,
        World world,
        Entity cell,
        CellSlot[] slots,
        int depth,
        ScheduleRegistry? schedule,
        ContextScope? scope)
    {
        Reconciler = reconciler;
        World = world;
        Cell = cell;
        Depth = depth;
        Schedule = schedule;
        Scope = scope;
        _slots = slots;
    }

    public readonly int NextSlotIndex {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _cursor;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetSlot(Entity entity) => _slots[_cursor++].Set(Reconciler, entity);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly Entity? PeekSlot() => Reconciler.Validate(_slots[_cursor]);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Advance() => _cursor++;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Skip(int count) => _cursor += count;

    public void DestroyRange(int count)
    {
        var slots = _slots;
        var end = _cursor + count;
        var result = Outcome<Exception>.Success;
        var reconciler = Reconciler;
        for (var i = _cursor; i < end; i++) {
            var slot = slots[i];
            slots[i] = default;
            result = result.Attempt(() => reconciler.DestroySlot(slot));
        }
        _cursor = end;
        result.ThrowIfFailed();
    }
}
