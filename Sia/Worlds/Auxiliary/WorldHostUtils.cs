using Microsoft.VisualBasic;

namespace Sia;

internal static class WorldHostUtils
{
    public struct EntityAddEventSender(EntityRef entity, WorldDispatcher dispatcher) : IGenericTypeHandler
    {
        public readonly void Handle<T>()
        {
            dispatcher.Send(entity, WorldEvents.Add<T>.Instance);
            dispatcher.Send(entity, WorldEvents.Set<T>.Instance);
        }
    }

    public struct EntityRemoveEventSender(EntityRef entity, WorldDispatcher dispatcher) : IGenericTypeHandler
    {
        public readonly void Handle<T>()
            => dispatcher.Send(entity, WorldEvents.Remove<T>.Instance);
    }

    public struct ExEntityRemoveEventSender(
        EntityRef entity, EntityDescriptor prevDesc, WorldDispatcher dispatcher) : IGenericTypeHandler
    {
        public readonly void Handle<T>()
        {
            if (prevDesc.Offsets.ContainsKey(typeof(T))) {
                dispatcher.Send(entity, WorldEvents.Remove<T>.Instance);
            }
        }
    }
}