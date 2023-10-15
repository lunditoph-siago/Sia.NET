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
        void Execute(World world, in EntityRef entity);
    }

    private class PureEventSender<UCommand> : IExecutor
        where UCommand : TCommand
    {
        public static readonly PureEventSender<UCommand> Instance = new();

        public void Execute(World world, in EntityRef entity)
            => world.Send(entity, PureEvent<UCommand>.Instance);
    }

    private class CustomAction : IExecutor
    {
        private readonly Action<World> _handler;

        public CustomAction(Action<World> handler)
        {
            _handler = handler;
        }

        public void Execute(World world, in EntityRef entity)
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

        public void Execute(World world, in EntityRef entity)
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

        public void Execute(World world, in EntityRef entity)
            => _action(world, _data);
    }

    public World World { get; }

    private readonly ThreadLocal<List<(IExecutor, EntityRef)>> _executors = new(() => new(), true);

    internal WorldCommandBuffer(World world)
    {
        World = world;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Modify<UCommand>(in EntityRef target, in UCommand command)
        where UCommand : TCommand
    {
        command.ExecuteOnParallel(target);
        _executors.Value!.Add((PureEventSender<UCommand>.Instance, target));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Do(Action<World> handler)
        => _executors.Value!.Add((new CustomAction(handler), default));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Do<TData>(in TData data, Handler<TData> handler)
        => _executors.Value!.Add((new CustomAction<TData>(data, handler), default));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Do<TData>(in TData data, SimpleHandler<TData> handler)
        => _executors.Value!.Add((new SimpleCustomAction<TData>(data, handler), default));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Submit()
    {
        foreach (var senders in _executors.Values) {
            int count = 0;
            try {
                foreach (var (sender, entity) in CollectionsMarshal.AsSpan(senders)) {
                    ++count;
                    sender.Execute(World, entity);
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