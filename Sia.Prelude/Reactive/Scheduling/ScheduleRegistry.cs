namespace Sia.Reactive;

using System.Collections.Immutable;

public sealed class ScheduleRegistry(ScheduleLabel label) : ISystemScheduleEntry
{
    internal readonly record struct Slot(
        Entity slotEntity,
        Entity ownerCell,
        int slotIndex,
        SystemChain.Entry entry,
        SystemStage runtime)
    {
        private readonly EntityReference _slotEntity = new(slotEntity);
        private readonly EntityReference _ownerCell = new(ownerCell);

        internal Entity SlotEntity => _slotEntity.GetOrDefault();
        internal Entity OwnerCell => _ownerCell.GetOrDefault();
        internal int SlotIndex { get; } = slotIndex;
        internal SystemChain.Entry Entry { get; } = entry;
        internal SystemStage Runtime { get; } = runtime;
    }

    public ScheduleLabel Label { get; } = label;
    public ExecutionPlan? CurrentPlan { get; internal set; }
    public int Version { get; internal set; }

    internal readonly List<Slot> Slots = [];
    internal ScheduleRegistration? Registration;
    internal bool RebuildQueued;
    internal int ScopeCount;
    internal ImmutableArray<SystemStage> RuntimeOrder = [];

    void IScheduleEntry.Tick()
    {
        foreach (var runtime in RuntimeOrder) {
            runtime.Tick();
        }
    }

    ExecutionPlan? ISystemScheduleEntry.Plan => CurrentPlan;

    void ISystemScheduleEntry.TickSystem(int index)
        => RuntimeOrder[index].Tick();

    internal SystemStage? Remove(Entity slotEntity)
    {
        for (var i = 0; i < Slots.Count; i++) {
            if (Slots[i].SlotEntity == slotEntity) {
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
