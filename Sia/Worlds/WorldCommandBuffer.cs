using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Sia;

public class WorldCommandBuffer<TCommand>
    where TCommand : ICommand
{
    private interface ISender
    {
        void Send(World world);
    }

    private class Sender<UCommand> : ISender
        where UCommand : TCommand
    {
        public EntityRef Target;
        public UCommand Command;

        public Sender(in EntityRef target, in UCommand command)
        {
            Target = target;
            Command = command;
        }

        public void Send(World world)
            => world.Send(Target, Command);
    }

    public World World { get; }

    private readonly ThreadLocal<List<ISender>> _senders = new(() => new(), true);

    internal WorldCommandBuffer(World world)
    {
        World = world;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Modify<UCommand>(in EntityRef target, in UCommand command)
        where UCommand : TCommand
    {
        command.Execute(World, target);
        _senders.Value!.Add(new Sender<UCommand>(target, command));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Submit()
    {
        foreach (var senders in _senders.Values) {
            foreach (var sender in CollectionsMarshal.AsSpan(senders)) {
                sender.Send(World);
            }
            senders.Clear();
        }
    }
}

public class WorldCommandBuffer : WorldCommandBuffer<ICommand>
{
    internal WorldCommandBuffer(World world)
        : base(world)
    {
    }
}