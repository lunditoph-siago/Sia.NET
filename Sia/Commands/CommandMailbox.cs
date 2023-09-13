namespace Sia;

using System.Runtime.InteropServices;

public class CommandMailbox<TTarget, TCommand> : IEventSender<TTarget, TCommand>
    where TTarget : notnull
    where TCommand : ICommand<TTarget>
{
    public delegate void Executor(in TTarget target, in TCommand command);

    public int Count => _commands.Count;

    private readonly List<CommandEntry> _commands = new();
    private readonly LinkedList<(TTarget, IDeferrableCommand<TTarget>)> _deferredCommands = new();

    private record struct CommandEntry(int Index, int Priority, TTarget Target, TCommand Command);

    private static Comparison<CommandEntry> CompareIndexedPriority { get; }
        = (e1, e2) => {
            int pc = e1.Priority.CompareTo(e2.Priority);
            return pc == 0 ? e1.Index.CompareTo(e2.Index) : pc;
        };

    private void Send<UCommand>((TTarget, UCommand) tuple)
        where UCommand : TCommand
        => Send(tuple.Item1, tuple.Item2);
    
    private static int GetCommandPriority(TCommand command)
        => command is ISortableCommand<TTarget> sortable ? sortable.Priority : 0;

    public void Send<UCommand>(in TTarget target, in UCommand command)
        where UCommand : TCommand
    {
        var converted = (TCommand)command;
        switch (converted) {
        case BatchedEvent<TCommand, TTarget> batchedCmd:
            batchedCmd.Events.ForEach(Send);
            batchedCmd.Dispose();
            return;
        default:
            _commands.Add(new(_commands.Count, GetCommandPriority(converted), target, converted));
            return;
        }
    }

    public void Execute(Executor executor)
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

            if (cmd is IDeferrableCommand<TTarget> deferCmd && deferCmd.ShouldDefer(target)) {
                _deferredCommands.AddLast((target, deferCmd));
                continue;
            }

            executor(target, cmd);
        }
    }
}