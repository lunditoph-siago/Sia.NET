namespace Sia.Reactive;

using System.Runtime.CompilerServices;

public ref struct GraphContext(
    Reconciler reconciler, World world, Entity cell, Entity?[] slots, int depth,
    ScheduleRegistry? schedule, ContextScope? scope)
{
    public readonly Reconciler Reconciler = reconciler;
    public readonly World World = world;
    public readonly Entity Cell = cell;
    public readonly int Depth = depth;

    public ScheduleRegistry? Schedule = schedule;
    public ContextScope? Scope = scope;

    private readonly Entity?[] _slots = slots;
    private int _cursor;

    public readonly int NextSlotIndex {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _cursor;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetSlot(Entity entity) => _slots[_cursor++] = entity;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly Entity? PeekSlot() => _slots[_cursor];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Advance() => _cursor++;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Skip(int count) => _cursor += count;

    public void DestroyRange(int count)
    {
        var slots = _slots;
        var end = _cursor + count;
        for (var i = _cursor; i < end; i++) {
            var entity = slots[i];
            if (entity is { IsValid: true }) {
                Reconciler.DestroySlot(entity);
            }
            slots[i] = null;
        }
        _cursor = end;
    }
}
