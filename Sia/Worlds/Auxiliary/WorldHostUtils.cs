using Microsoft.VisualBasic;

namespace Sia;

internal static class WorldHostUtils
{
    public struct EntityAddEventSender(EntityRef entity, WorldDispatcher dispatcher) : IGenericHandler<IHList>
    {
        private struct HeadSender(EntityRef entity, WorldDispatcher dispatcher) : IGenericHandler
        {
            public readonly void Handle<T>(in T value)
                => dispatcher.Send(entity, WorldEvents.Add<T>.Instance);
        }

        private HeadSender _headSender = new(entity, dispatcher);

        public readonly void Handle<T>(in T value) where T : IHList
        {
            value.HandleHead(_headSender);
            value.HandleTail(this);
        }
    }

    public struct EntityRemoveEventSender(EntityRef entity, WorldDispatcher dispatcher) : IGenericTypeHandler
    {
        public readonly void Handle<T>()
            => dispatcher.Send(entity, WorldEvents.Remove<T>.Instance);
    }
}