namespace Sia;

using System.Collections.Concurrent;

public readonly record struct CommandRequest(Entity Target, ICommand Command)
{
    public readonly void Apply(World world)
        => world.Execute(Target, Command);
}

public interface IWorldCommandQueue
{
    int Count { get; }

    void Enqueue(in CommandRequest request);
    int Apply(World world);
}

public sealed class WorldCommandQueue : IWorldCommandQueue
{
    private readonly ConcurrentQueue<CommandRequest> _requests = new();

    public int Count => _requests.Count;

    public void Enqueue(in CommandRequest request)
        => _requests.Enqueue(request);

    public int Apply(World world)
    {
        var count = 0;
        while (_requests.TryDequeue(out var request)) {
            request.Apply(world);
            count++;
        }
        return count;
    }
}
