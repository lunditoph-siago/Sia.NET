namespace Sia;

using System.Runtime.CompilerServices;

internal sealed class EntityState
{
    public IEntityHost? Host;
    public int Slot = -1;
    public ulong Token;
}

internal sealed class EntityStatePool
{
    private const int EntityIdBlockSize = 256;

    private EntityState[] _states = [];
    private int _count;
    private int _nextId;
    private int _remainingIds;
    private bool _retired;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Entity Rent()
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
        return new Entity(state, generation, NextId());
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
