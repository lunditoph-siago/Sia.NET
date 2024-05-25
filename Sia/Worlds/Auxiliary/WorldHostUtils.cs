using Microsoft.VisualBasic;

namespace Sia;

internal static class WorldHostUtils
{
    public struct EntityAddEventSender(EntityRef entity, WorldDispatcher dispatcher) : IGenericTypeHandler
    {
        public readonly void Handle<T>()
            => dispatcher.Send(entity, WorldEvents.Add<T>.Instance);
    }

    public struct EntityRemoveEventSender(EntityRef entity, WorldDispatcher dispatcher) : IGenericTypeHandler
    {
        public readonly void Handle<T>()
            => dispatcher.Send(entity, WorldEvents.Remove<T>.Instance);
    }
}