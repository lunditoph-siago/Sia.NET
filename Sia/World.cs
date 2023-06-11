namespace Sia;

using System.Runtime.InteropServices;

public static class WorldCommands
{
    public class Add : ICommand
    {
        public static Add Instance { get; } = new();
        private Add() {}
        public void Dispose() {}
    }

    public class Remove : ICommand
    {
        public static Remove Instance { get; } = new();
        private Remove() {}
        public void Dispose() {}
    }
}

public class World<T> : Group<T>
    where T : notnull
{
    public class Group : Group<T>
    {
        public World<T> World { get; }
        public Predicate<T>? Predicate { get; }

        internal int Index { get; set; }

        internal Group(World<T> world, Predicate<T>? predicate)
        {
            World = world;
            Predicate = predicate;
        }
    }

    public Dispatcher<T> Dispatcher { get; } = new();

    public IReadOnlyList<Group> Groups => _groups;

    private List<Group> _groups = new();

    public Group CreateGroup(Predicate<T>? predicate = null)
    {
        var group = new Group(this, predicate);
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

    public bool RemoveGroup(Group group)
    {
        if (group.World != this) {
            return false;
        }

        int index = group.Index;
        if (index < 0 || index >= _groups.Count || _groups[group.Index] != group) {
            return false;
        }

        int lastIndex = group.Count - 1;
        if (index != lastIndex) {
            _groups[index] = _groups[lastIndex];
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

    public virtual void Modify(T target, IExecutableCommand<T> command)
    {
        command.Execute(target);
        Dispatcher.Send(target, command);
    }
}

public class World : World<EntityRef>
{
}