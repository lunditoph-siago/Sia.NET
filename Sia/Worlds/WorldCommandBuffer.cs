using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Sia;

public class WorldCommandBuffer<TCommand>
    where TCommand : IParallelCommand
{
    public delegate void Handler<TData>(World world, in TData data);
    public delegate void SimpleHandler<TData>(World world, TData data);
    
    private interface IExecutor
    {
        void Execute(World world);
    }

    private class Sender<UCommand> : IExecutor
        where UCommand : TCommand
    {
        private readonly EntityRef _target;
        private readonly UCommand _command;

        public Sender(in EntityRef target, in UCommand command)
        {
            _target = target;
            _command = command;
        }

        public void Execute(World world)
            => world.Send(_target, _command);
    }

    private class CustomAction : IExecutor
    {
        private readonly Action<World> _handler;

        public CustomAction(Action<World> handler)
        {
            _handler = handler;
        }

        public void Execute(World world)
            => _handler(world);
    }

    private class CustomAction<TData> : IExecutor
    {
        private readonly TData _data;
        private readonly Handler<TData> _action;

        public CustomAction(in TData data, Handler<TData> action)
        {
            _data = data;
            _action = action;
        }

        public void Execute(World world)
            => _action(world, _data);
    }

    private class SimpleCustomAction<TData> : IExecutor
    {
        private readonly TData _data;
        private readonly SimpleHandler<TData> _action;

        public SimpleCustomAction(in TData data, SimpleHandler<TData> action)
        {
            _data = data;
            _action = action;
        }

        public void Execute(World world)
            => _action(world, _data);
    }

    public World World { get; }

    private readonly ThreadLocal<List<IExecutor>> _executors = new(() => new(), true);

    internal WorldCommandBuffer(World world)
    {
        World = world;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Modify<UCommand>(in EntityRef target, in UCommand command)
        where UCommand : TCommand
    {
        command.ExecuteOnParallel(target);
        _executors.Value!.Add(new Sender<UCommand>(target, command));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Do(Action<World> handler)
        => _executors.Value!.Add(new CustomAction(handler));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Do<TData>(in TData data, Handler<TData> handler)
        => _executors.Value!.Add(new CustomAction<TData>(data, handler));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Do<TData>(in TData data, SimpleHandler<TData> handler)
        => _executors.Value!.Add(new SimpleCustomAction<TData>(data, handler));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Submit()
    {
        foreach (var senders in _executors.Values) {
            int count = 0;
            try {
                foreach (var sender in CollectionsMarshal.AsSpan(senders)) {
                    ++count;
                    sender.Execute(World);
                }
            }
            catch {
                senders.RemoveRange(0, count);
                throw;
            }
            senders.Clear();
        }
    }
}

public class WorldCommandBuffer : WorldCommandBuffer<IParallelCommand>
{
    internal WorldCommandBuffer(World world)
        : base(world)
    {
    }
}