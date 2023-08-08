namespace Sia;

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

public class World<T> : Group<T>, IDisposable
    where T : notnull
{
    public event Action<World<T>>? OnDisposed;

    public bool IsDisposed { get; private set; }

    public WorldDispatcher<T> Dispatcher { get; }

    public IReadOnlyList<WorldGroup<T>> Groups => _groups;

    private readonly List<WorldGroup<T>> _groups = new();
    private readonly SparseSet<object> _singletons = new();

    public World()
    {
        Dispatcher = new WorldDispatcher<T>(this);
    }

    public WorldGroup<T> CreateGroup(Predicate<T>? predicate = null)
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

    public virtual void Modify(in T target, ICommand<T> command)
    {
        command.Execute(this, target);
        Dispatcher.Send(target, command);
    }

    public ref TSingleton Acquire<TSingleton>()
        where TSingleton : struct, IConstructable
    {
        ref var box = ref _singletons.GetOrAddValueRef(
            WorldSingletonIndexer<TSingleton>.Index, out bool exists);

        if (exists) {
            return ref Unsafe.Unbox<TSingleton>(box);
        }

        box = new TSingleton();
        ref var singleton = ref Unsafe.Unbox<TSingleton>(box);
        singleton.Construct();
        return ref singleton;
    }

    public unsafe ref TSingleton Add<TSingleton>()
        where TSingleton : struct
    {
        ref var box = ref _singletons.GetOrAddValueRef(
            WorldSingletonIndexer<TSingleton>.Index, out bool exists);

        if (exists) {
            throw new Exception("Singleton already exists: " + typeof(TSingleton));
        }
        return ref Unsafe.Unbox<TSingleton>(box);
    }

    public bool Remove<TSingleton>()
        where TSingleton : struct
        => _singletons.Remove(WorldSingletonIndexer<TSingleton>.Index);

    public unsafe ref TSingleton Get<TSingleton>()
        where TSingleton : struct
    {
        ref var box = ref _singletons.GetValueRefOrNullRef(
            WorldSingletonIndexer<TSingleton>.Index);

        if (Unsafe.IsNullRef(ref box)) {
            throw new Exception("Singleton not found: " + typeof(TSingleton));
        }
        return ref Unsafe.Unbox<TSingleton>(box);
    }
    
    public unsafe ref TSingleton GetOrNullRef<TSingleton>()
        where TSingleton : struct
    {
        ref var box = ref _singletons.GetValueRefOrNullRef(
            WorldSingletonIndexer<TSingleton>.Index);

        if (Unsafe.IsNullRef(ref box)) {
            return ref Unsafe.NullRef<TSingleton>();
        }
        return ref Unsafe.Unbox<TSingleton>(box);
    }

    public bool Contains<TSingleton>()
        => _singletons.ContainsKey(WorldSingletonIndexer<TSingleton>.Index);

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