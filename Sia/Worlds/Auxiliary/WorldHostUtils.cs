namespace Sia;

internal static class WorldHostUtils
{
    internal static void EnsureCanMoveIn(this IEntityHost host)
        => (host as IEntityMoveDestination)?.EnsureCanMoveIn();

    internal static void ReleaseOwned(this IEntityHost host, Entity entity)
    {
        if (host is IWorldBatchHost worldHost) {
            worldHost.ReleaseOwned(entity);
        }
        else {
            host.Release(entity);
        }
    }

    public struct EntityAddEventSender(Entity entity, WorldDispatcher dispatcher) : IGenericTypeHandler
    {
        public readonly void Handle<T>()
            => dispatcher.SendAddAndSet<T>(entity);
    }

    public struct EntityRemoveEventSender(Entity entity, WorldDispatcher dispatcher) : IGenericTypeHandler
    {
        public readonly void Handle<T>()
            => dispatcher.Send(entity, WorldEvents.Remove<T>.Instance);
    }

    public struct ExEntityRemoveEventSender(
        Entity entity, EntityDescriptor prevDesc, WorldDispatcher dispatcher) : IGenericTypeHandler
    {
        public readonly void Handle<T>()
        {
            if (prevDesc.Offsets.ContainsKey(typeof(T))) {
                dispatcher.Send(entity, WorldEvents.Remove<T>.Instance);
            }
        }
    }
}
