namespace Sia;

using System.Runtime.InteropServices;

public class WorldChannel<TCommand, TTarget>
    where TCommand : ICommand, IExecutable<TTarget>
    where TTarget : notnull
{
    public int Count => _commands.Count;
    public World<TTarget> World { get; }

    private List<CommandEntry> _commands = new();
    private Dictionary<(TTarget, Type, uint), int> _commandMap = new();
    private LinkedList<(TTarget, IDeferrable<TTarget>)> _deferredCommands = new();

    private record struct CommandEntry(int Index, TTarget Target, TCommand Command);

    private static Comparison<CommandEntry> CompareIndexedPriority { get; }
        = (e1, e2) => {
            var p1 = e1.Command is ISortable s1 ? s1.Priority : 0;
            var p2 = e2.Command is ISortable s2 ? s2.Priority : 0;
            var c = p1.CompareTo(p2);
            return c == 0 ? e1.Index.CompareTo(e2.Index) : c;
        };

    public WorldChannel(World<TTarget> world)
    {
        World = world;
    }

    private void Send((TTarget, TCommand) tuple)
        => Send(tuple.Item1, tuple.Item2);

    public virtual void Send(TTarget target, TCommand command)
    {
        switch (command) {
        case BatchedCommand<TCommand, TTarget> batchedCmd:
            batchedCmd.Commands.ForEach(Send);
            batchedCmd.Dispose();
            return;
        case IMergeable mergableCmd when mergableCmd.Id != null:
            var key = (target, command.GetType(), mergableCmd.Id.Value);
            if (_commandMap.TryGetValue(key, out var index)) {
                var entry = _commands[index];
                var cmd = entry.Command;
                mergableCmd.Merge(cmd);
                cmd.Dispose();
                _commands[index] = new(entry.Index, entry.Target, command);
            }
            else {
                index = _commands.Count;
                _commands.Add(new(index, target, command));
                _commandMap.Add(key, index);
            }
            return;
        default:
            _commands.Add(new(_commands.Count, target, command));
            return;
        }
    }

    public virtual void Execute()
    {
        var deferredCmdNode = _deferredCommands.First;
        while (deferredCmdNode != null) {
            var (target, cmd) = deferredCmdNode.Value;
            var nextNode = deferredCmdNode.Next;
            if (!cmd.ShouldDefer(target)) {
                World.Modify(target, cmd);
                _deferredCommands.Remove(deferredCmdNode);
            }
            deferredCmdNode = nextNode;
        }

        var span = CollectionsMarshal.AsSpan(_commands);
        _commands.Sort(CompareIndexedPriority);

        foreach (var (_, target, cmd) in span) {
            if (cmd is IDeferrable<TTarget> deferCmd && deferCmd.ShouldDefer(target)) {
                _deferredCommands.AddLast((target, deferCmd));
                continue;
            }
            World.Modify(target, cmd);
        }
    }
}

public class WorldChannel<TCommand> : WorldChannel<TCommand, EntityRef>
    where TCommand : ICommand, IExecutable<EntityRef>
{
    public WorldChannel(World<EntityRef> world) : base(world)
    {
    }
}

public class WorldChannel : WorldChannel<IExecutable<EntityRef>>
{
    public WorldChannel(World<EntityRef> world) : base(world)
    {
    }
}