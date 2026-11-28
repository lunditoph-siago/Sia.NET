using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

namespace Sia;

internal sealed class EntityState
{
    public IEntityHost? Host;
    public IEventSink<Entity>? EventSink;
    public int Slot = -1;
    public bool Releasing;
    public bool NotifyingMigration;
    public ulong Token;

    public bool IsTransitioning => Releasing || NotifyingMigration;
}

internal sealed class EntityStatePool
{
    private const int EntityIdBlockSize = 256;

    private EntityState[] _states = [];
    private int _count;
    private int _nextId;
    private int _remainingIds;
    private bool _retired;

#if !BROWSER
    internal bool CanRentFreshRange => !_retired && _remainingIds == 0 && _count == 0;

    internal static void RentFreshMany(ParallelRunner runner, EntityStorage destination, IEntityHost host, ref int completed)
    {
        var count = destination.Count;
        var firstId = EntityId.Reserve(count);
        var split = count / 2;
        var left = 0;
        var right = 0;
        var completion = RunnerBarrier.GetForScopedWork();
        Exception? failure = null;
        try {
            // The caller owns the first half. A single submitted job owns the second.
            runner.Run(1, _ => InitializeFresh(destination.AsSpan()[split..],
                host, split, unchecked(firstId + split), ref right), completion);
            InitializeFresh(destination.AsSpan()[..split], host, 0, firstId, ref left);
        }
        catch (Exception exception) {
            failure = exception;
        }
        try {
            completion.WaitAndReturnScopedWork();
        }
        catch (Exception exception) {
            failure ??= exception;
        }

        completed = left == split ? left + right : left;
        if (failure is not null) ExceptionDispatchInfo.Throw(failure);
    }

    private static void InitializeFresh(Span<Entity> destination, IEntityHost host, int firstSlot, int firstId, ref int completed)
    {
        var i = 0;
        try {
            for (; i < destination.Length; i++) {
                destination[i] = new Entity(new EntityState { Host = host, Slot = firstSlot + i }, 1, new(unchecked(firstId + i)));
            }
        }
        finally {
            completed = i;
        }
    }
#endif

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Entity Rent(IEntityHost? host = null, int slot = -1)
    {
        ObjectDisposedException.ThrowIf(_retired, this);
        EntityState state;
        if (_count != 0) {
            var index = --_count;
            state = _states[index];
        }
        else {
            state = new EntityState();
        }
        var generation = (uint)(state.Token >> 32) + 1;
        state.Host = host;
        state.Slot = slot;
        return new Entity(state, generation, NextId());
    }

    internal void RentMany(Span<Entity> entities, IEntityHost host, int firstSlot, ref int completed)
    {
        ObjectDisposedException.ThrowIf(_retired, this);
        var i = 0;
        try {
            for (; i < entities.Length; i++) {
                entities[i] = Rent(host, firstSlot + i);
            }
        }
        finally {
            completed = i;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private EntityId NextId()
    {
        if (_remainingIds == 0) {
            _nextId = EntityId.Reserve(EntityIdBlockSize);
            _remainingIds = EntityIdBlockSize;
        }
        _remainingIds--;
        return new(_nextId++);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Return(EntityState state)
    {
        state.EventSink = null;
        state.Host = null;
        state.Slot = -1;
        if ((uint)(state.Token >> 32) == uint.MaxValue || _retired) {
            return;
        }
        if (_count == _states.Length) {
            Array.Resize(ref _states, _count == 0 ? 4 : _count * 2);
        }
        _states[_count++] = state;
    }

    public void Retire()
    {
        _retired = true;
        Array.Clear(_states);
        _count = 0;
    }
}
