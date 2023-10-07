using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

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

    private readonly ConcurrentQueue<ISender> _queue = new();

    internal WorldCommandBuffer(World world)
    {
        World = world;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Modify<UCommand>(in EntityRef target, in UCommand command)
        where UCommand : TCommand
    {
        command.Execute(World, target);
        _queue.Enqueue(new Sender<UCommand>(target, command));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Submit()
    {
        while (_queue.TryDequeue(out var sender)) {
            sender.Send(World);
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