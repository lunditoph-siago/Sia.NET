namespace Sia.Reactive;

using System.Runtime.CompilerServices;

public ref struct GraphContext(
    Reconciler reconciler,
    Entity cell,
    CellSlot[] slots,
    int depth,
    ScheduleRegistry? schedule,
    ContextScope? scope)
{
    public readonly Reconciler Reconciler = reconciler;
    public readonly World World => Reconciler.World;
    public readonly Entity Cell = cell;
    public readonly int Depth = depth;

    public ScheduleRegistry? Schedule = schedule;
    public ContextScope? Scope = scope;
    internal Entity Output;
    internal Entity MessageOwner;

    private readonly CellSlot[] _slots = slots;
    private int _cursor;

    public readonly int NextSlotIndex {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _cursor;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetSlot(Entity entity) => _slots[_cursor++].Entity = entity;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly Entity PeekSlot() => Reconciler.Validate(_slots[_cursor]);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Advance() => _cursor++;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Skip(int count) => _cursor += count;

    internal void RewindTo(int index) => _cursor = index;

    public void RemountRange(int count)
    {
        var start = _cursor;
        DestroyRange(count);
        RewindTo(start);
    }

    public void DestroyRange(int count)
    {
        var slots = _slots;
        var end = _cursor + count;
        var result = Outcome<Exception>.Success;
        var reconciler = Reconciler;
        for (var i = _cursor; i < end; i++) {
            var slot = slots[i];
            slots[i] = default;
            var operation = (Owner: reconciler, Slot: slot);
            result = result.Attempt(
                operation,
                static (in (Reconciler Owner, CellSlot Slot) value)
                    => value.Owner.DestroySlot(value.Slot));
        }
        _cursor = end;
        result.ThrowIfFailed();
    }
}
