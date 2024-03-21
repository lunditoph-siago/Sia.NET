namespace Sia;

internal static class WorldHostUtils
{
    public struct EntityHeadAddEventSender(EntityRef entity, WorldDispatcher dispatcher) : IGenericHandler
    {
        public readonly void Handle<T>(in T value)
        {
            dispatcher.Send(entity, WorldEvents.Add<T>.Instance);
        }
    }

    public struct EntityTailAddEventSender(EntityRef entity, WorldDispatcher dispatcher) : IGenericHandler<IHList>
    {
        public readonly void Handle<T>(in T value) where T : IHList
        {
            value.HandleHead(new EntityHeadAddEventSender(entity, dispatcher));
            value.HandleTail(new EntityTailAddEventSender(entity, dispatcher));
        }
    }

    public struct EntityHeadRemoveEventSender(EntityRef entity, WorldDispatcher dispatcher) : IGenericHandler
    {
        public readonly void Handle<T>(in T value)
        {
            dispatcher.Send(entity, WorldEvents.Remove<T>.Instance);
        }
    }

    public struct EntityTailRemoveEventSender(EntityRef entity, WorldDispatcher dispatcher) : IGenericHandler<IHList>
    {
        public readonly void Handle<T>(in T value) where T : IHList
        {
            value.HandleHead(new EntityHeadRemoveEventSender(entity, dispatcher));
            value.HandleTail(new EntityTailRemoveEventSender(entity, dispatcher));
        }
    }
}