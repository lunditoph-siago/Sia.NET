namespace Sia;

using System.Runtime.InteropServices;

public class CommandMailbox<TCommand, TTarget> : ICommandSender<TCommand, TTarget>
    where TCommand : ICommand
    where TTarget : notnull
{
    public int Count => _commands.Count;

    private List<CommandEntry> _commands = new();
    private LinkedList<(TTarget, IDeferrable<TTarget>)> _deferredCommands = new();

    private record struct CommandEntry(int Index, int Priority, TTarget Target, TCommand Command);

    private static Comparison<CommandEntry> CompareIndexedPriority { get; }
        = (e1, e2) => {
            int pc = e1.Priority.CompareTo(e2.Priority);
            return pc == 0 ? e1.Index.CompareTo(e2.Index) : pc;
        };

    private void Send((TTarget, TCommand) tuple)
        => Send(tuple.Item1, tuple.Item2);
    
    private int GetCommandPriority(TCommand command)
        => command is ISortable sortable ? sortable.Priority : 0;

    public virtual void Send(TTarget target, TCommand command)
    {
        switch (command) {
        case BatchedCommand<TCommand, TTarget> batchedCmd:
            batchedCmd.Commands.ForEach(Send);
            batchedCmd.Dispose();
            return;
        default:
            _commands.Add(new(_commands.Count, GetCommandPriority(command), target, command));
            return;
        }
    }

    public virtual void Execute(Action<TTarget, TCommand> executor)
    {
        var deferredCmdNode = _deferredCommands.First;
        while (deferredCmdNode != null) {
            var (target, cmd) = deferredCmdNode.Value;
            var nextNode = deferredCmdNode.Next;

            if (!cmd.ShouldDefer(target)) {
                executor(target, (TCommand)cmd);
                _deferredCommands.Remove(deferredCmdNode);
            }

            deferredCmdNode = nextNode;
        }

        var span = CollectionsMarshal.AsSpan(_commands);
        _commands.Sort(CompareIndexedPriority);

        foreach (ref var entry in span) {
            var cmd = entry.Command;
            var target = entry.Target;

            if (cmd is IDeferrable<TTarget> deferCmd && deferCmd.ShouldDefer(target)) {
                _deferredCommands.AddLast((target, deferCmd));
                continue;
            }

            executor(target, cmd);
        }
    }
}

public class CommandMailbox<TCommand> : CommandMailbox<TCommand, EntityRef>, ICommandSender<TCommand>
    where TCommand : ICommand, IExecutable<EntityRef>
{
}

public class CommandMailbox : CommandMailbox<IExecutable<EntityRef>>
{
}