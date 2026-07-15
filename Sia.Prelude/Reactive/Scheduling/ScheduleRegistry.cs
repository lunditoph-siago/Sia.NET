namespace Sia.Reactive;

using System.Collections.Immutable;

public sealed class ScheduleRegistry(ScheduleLabel label) : IScheduleEntry
{
    internal readonly record struct Slot(
        Entity SlotEntity,
        Entity OwnerCell,
        int SlotIndex,
        SystemChain.Entry Entry,
        SystemStage Runtime);

    public ScheduleLabel Label { get; } = label;
    public ExecutionPlan? CurrentPlan { get; internal set; }
    public int Version { get; internal set; }

    internal readonly List<Slot> Slots = [];
    internal ScheduleRegistration? Registration;
    internal bool RebuildQueued;
    internal ImmutableArray<SystemStage> RuntimeOrder = [];

    void IScheduleEntry.Tick()
    {
        foreach (var runtime in RuntimeOrder) {
            runtime.Tick();
        }
    }

    internal SystemStage? Remove(Entity slotEntity)
    {
        for (var i = 0; i < Slots.Count; i++) {
            if (ReferenceEquals(Slots[i].SlotEntity, slotEntity)) {
                var runtime = Slots[i].Runtime;
                Slots.RemoveAt(i);
                return runtime;
            }
        }
        return null;
    }
}

public readonly record struct SystemNode(ScheduleRegistry Registry);
public readonly record struct ScheduleNode(ScheduleRegistry Registry);
