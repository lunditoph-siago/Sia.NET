namespace Sia;

using System.Runtime.InteropServices;

public class World<T> : Group<T>
    where T : notnull
{
    public WorldDispatcher<T> Dispatcher { get; }

    public IReadOnlyList<WorldGroup<T>> Groups => _groups;

    private List<WorldGroup<T>> _groups = new();

    public World()
    {
        Dispatcher = new WorldDispatcher<T>(this);
    }

    public WorldGroup<T> CreateGroup(Predicate<T>? predicate = null)
    {
        var group = new WorldGroup<T>(this, predicate);
        group.Index = _groups.Count;
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
        Dispatcher.Send(value, WorldCommands.Add.Instance);
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
        Dispatcher.Send(value, WorldCommands.Remove.Instance);
        return true;
    }

    public override void Clear()
    {
        if (Count == 0) {
            return;
        }

        foreach (ref var value in AsSpan()) {
            Dispatcher.Send(value, WorldCommands.Remove.Instance);
        }
        base.Clear();

        foreach (var group in CollectionsMarshal.AsSpan(_groups)) {
            group.Clear();
        }
    }

    public virtual void Modify(T target, IExecutable<T> command)
    {
        command.Execute(target);
        Dispatcher.Send(target, command);
    }
}

public class World : World<EntityRef>
{
}