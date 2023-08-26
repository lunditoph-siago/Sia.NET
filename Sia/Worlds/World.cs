namespace Sia;

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

public class World<T> : Group<T>, IEventSender<IEvent, T>, IDisposable
    where T : notnull
{
    public delegate bool GroupPredicate(in T target);

    public event Action<World<T>>? OnDisposed;

    public bool IsDisposed { get; private set; }

    public WorldDispatcher<T> Dispatcher { get; }

    public IReadOnlyList<WorldGroup<T>> Groups => _groups;

    private readonly List<WorldGroup<T>> _groups = new();
    private readonly SparseSet<object> _addons = new(256, 256);

    public World()
    {
        Dispatcher = new WorldDispatcher<T>(this);
    }

    public WorldGroup<T> CreateGroup(GroupPredicate? predicate = null)
    {
        var group = new WorldGroup<T>(this, predicate) {
            Index = _groups.Count
        };
        _groups.Add(group);

        if (predicate == null) {
            foreach (ref var v in AsSpan()) {
                group.Add(v);
            }
        }
        else {
            foreach (ref var v in AsSpan()) {
                if (predicate(v)) {
                    group.Add(v);
                }
            }
        }

        return group;
    }

    public bool RemoveGroup(WorldGroup<T> group)
    {
        if (group.World != this) {
            return false;
        }

        int index = group.Index;
        if (index < 0 || index >= _groups.Count || _groups[group.Index] != group) {
            return false;
        }

        int lastIndex = _groups.Count - 1;
        if (index != lastIndex) {
            var lastGroup = _groups[lastIndex];
            _groups[index] = lastGroup;
            lastGroup.Index = index;
        }
        _groups.RemoveAt(lastIndex);
        return true;
    }

    public override bool Add(in T value)
    {
        if (!base.Add(value)) {
            return false;
        }
        foreach (var group in CollectionsMarshal.AsSpan(_groups)) {
            if (group.Predicate == null || group.Predicate(value)) {
                group.Add(value);
            }
        }
        Dispatcher.Send(value, WorldEvents.Add.Instance);
        return true;
    }

    public override bool Remove(in T value)
    {
        if (!base.Remove(value)) {
            return false;
        }
        foreach (var group in CollectionsMarshal.AsSpan(_groups)) {
            group.Remove(value);
        }
        Dispatcher.Send(value, WorldEvents.Remove.Instance);
        return true;
    }

    public override void Clear()
    {
        if (Count == 0) {
            return;
        }

        foreach (ref var value in AsSpan()) {
            Dispatcher.Send(value, WorldEvents.Remove.Instance);
        }
        base.Clear();

        foreach (var group in CollectionsMarshal.AsSpan(_groups)) {
            group.Clear();
        }
    }

    public void Send(in T target, IEvent e)
        => Dispatcher.Send(target, e);

    public virtual void Modify<TCommand>(in T target, in TCommand command)
        where TCommand : ICommand<T>
    {
        command.Execute(this, target);
        Dispatcher.Send(target, command);
    }

    public TAddon AcquireAddon<TAddon>()
        where TAddon : class, new()
    {
        ref var addon = ref _addons.GetOrAddValueRef(
            WorldAddonIndexer<TAddon>.Index, out bool exists);

        if (exists) {
            return (TAddon)addon;
        }

        var newAddon = new TAddon();
        addon = newAddon;
        return newAddon;
    }

    public TAddon AddAddon<TAddon>()
        where TAddon : class, new()
    {
        ref var singleton = ref _addons.GetOrAddValueRef(
            WorldAddonIndexer<TAddon>.Index, out bool exists);

        if (exists) {
            throw new Exception("Addon already exists: " + typeof(TAddon));
        }

        var newSingleton = new TAddon();
        singleton = newSingleton;
        return newSingleton;
    }

    public bool RemoveAddon<TAddon>()
        where TAddon : struct
        => _addons.Remove(WorldAddonIndexer<TAddon>.Index);

    public TAddon Get<TAddon>()
        where TAddon : class
    {
        ref var addon = ref _addons.GetValueRefOrNullRef(
            WorldAddonIndexer<TAddon>.Index);

        if (Unsafe.IsNullRef(ref addon)) {
            throw new Exception("Addon not found: " + typeof(TAddon));
        }
        return (TAddon)addon;
    }

    public bool ContainsAddon<Addon>()
        => _addons.ContainsKey(WorldAddonIndexer<Addon>.Index);

    protected virtual void Dispose(bool disposing)
    {
        if (IsDisposed) { return; }
        IsDisposed = true;
        OnDisposed?.Invoke(this);
    }

    ~World()
    {
        Dispose(false);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}

public class World : World<EntityRef>
{
}