namespace Sia.Reactive;

public sealed class ScheduleRegistry(Type labelType)
{
    internal struct Slot
    {
        public Entity SlotEntity;
        public Entity OwnerCell;
        public int SlotIndex;
        public SystemChain.Entry Entry;
    }

    public Type LabelType { get; } = labelType;
    public SystemStage? Stage { get; internal set; }
    public int Version { get; internal set; }

    internal readonly List<Slot> Slots = [];
    internal bool RebuildQueued;

    internal void Remove(Entity slotEntity)
    {
        var slots = Slots;
        for (var i = 0; i < slots.Count; i++) {
            if (ReferenceEquals(slots[i].SlotEntity, slotEntity)) {
                slots.RemoveAt(i);
                return;
            }
        }
    }
}

public struct SystemNode
{
    public ScheduleRegistry Registry;
}

public struct ScheduleNode
{
    public ScheduleRegistry Registry;
}
