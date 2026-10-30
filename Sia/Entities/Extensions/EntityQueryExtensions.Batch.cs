using System.Buffers;

namespace Sia;

public static partial class EntityQueryExtensions
{
    private readonly record struct HostSnapshot(IEntityHost Host, int Start, int Count);

    public static void Add<TComponent>(this IEntityQuery query)
        => query.Add(default(TComponent)!);

    public static void Add<TComponent>(this IEntityQuery query, in TComponent initial)
        => Change(query, initial,
            static (host, entities, value) => EntityBatch.TryAdd(host, entities, value),
            static (host, entities, value) => {
                if (host is IWorldBatchHost { IsExactArrayHost: true } || !host.TryAddAll(entities, value))
                    foreach (var entity in entities) entity.Add(value);
            });

    public static void Remove<TComponent>(this IEntityQuery query)
        => Change(query, default(TComponent),
            static (host, entities, _) => EntityBatch.TryRemove<TComponent>(host, entities),
            static (host, entities, _) => {
                if (host is IWorldBatchHost { IsExactArrayHost: true } || !host.TryRemoveAll<TComponent>(entities))
                    foreach (var entity in entities) entity.Remove<TComponent>();
            });

    private delegate bool TryHostChange<T>(IEntityHost host, ReadOnlySpan<Entity> entities, T value);
    private delegate void EntityChange<T>(IEntityHost host, ReadOnlySpan<Entity> entities, T value);

    private static void Change<T>(IEntityQuery query, T value, TryHostChange<T> tryChange, EntityChange<T> change)
    {
        ArgumentNullException.ThrowIfNull(query);
        var hosts = query.Hosts;
        var hostCount = hosts.Count;
        if (hostCount == 0) return;
        var snapshots = ArrayPool<HostSnapshot>.Shared.Rent(hostCount);
        try {
            var count = 0;
            var onlyHost = -1;
            var direct = true;
            for (var i = 0; i < hostCount; i++) {
                var host = hosts[i];
                var length = host.Count;
                snapshots[i] = new(host, count, length);
                count = checked(count + length);
                if (host is not IWorldBatchHost { IsExactArrayHost: true }) direct = false;
                if (length != 0) {
                    if (onlyHost != -1) direct = false;
                    onlyHost = i;
                }
            }
            if (count == 0) return;

            var attempted = false;
            if (direct) {
                var snapshot = snapshots[onlyHost];
                var live = snapshot.Host.UnsafeGetEntitySpan();
                if (live.Length == snapshot.Count) {
                    attempted = true;
                    if (tryChange(snapshot.Host, live, value)) return;
                }
            }

            var entities = ArrayPool<Entity>.Shared.Rent(count);
            try {
                foreach (var snapshot in snapshots.AsSpan(0, hostCount)) {
                    snapshot.Host.UnsafeGetEntitySpan()[..snapshot.Count].CopyTo(entities.AsSpan(snapshot.Start));
                }
                foreach (var snapshot in snapshots.AsSpan(0, hostCount)) {
                    if (snapshot.Count != 0) {
                        var span = entities.AsSpan(snapshot.Start, snapshot.Count);
                        if (attempted || !tryChange(snapshot.Host, span, value)) change(snapshot.Host, span, value);
                    }
                }
            }
            finally {
                ArrayPool<Entity>.Shared.Return(entities, clearArray: true);
            }
        }
        finally {
            ArrayPool<HostSnapshot>.Shared.Return(snapshots, clearArray: true);
        }
    }
}
