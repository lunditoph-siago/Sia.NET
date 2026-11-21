using System.Threading.Channels;

namespace Sia;

internal sealed class ExtractQuery : IEntityQuery
{
    public int Version => _inner.Version;
    public IReadOnlyList<IEntityHost> Hosts => _inner.Hosts;

    private readonly IEntityQuery _inner;

    internal ExtractQuery(IEntityQuery inner)
        => _inner = inner;

    public void Dispose() {}
}

public abstract class ExtractSystemBase(
    IEntityMatcher? matcher = null,
    IEventUnion? trigger = null,
    IEventUnion? filter = null,
    SystemChain? children = null,
    IEntityMatcher? extractMatcher = null)
    : SystemBase(matcher, trigger, filter, children)
{
    private enum InitializationPhase { Idle, Stage, Query }

    private InitializationPhase _initialization;
    private IEntityQuery _parentQuery = null!;
    private ExtractQuery _extract = null!;

    internal void InitializeForStage(World world)
    {
        if (_initialization != InitializationPhase.Idle || _parentQuery is not null) {
            throw new InvalidOperationException("The extract system is already initialized.");
        }

        _initialization = InitializationPhase.Stage;
        try {
            ((ISystem)this).Initialize(world);
        }
        catch (Exception error) {
            Outcome<Exception>.Failure(error)
                .Attempt(this, static (in system) => system.ReleaseQuery())
                .ThrowFailure();
        }
        finally {
            _initialization = InitializationPhase.Idle;
        }
    }

    public override void Initialize(World world)
    {
        if (_initialization == InitializationPhase.Query || _parentQuery is not null) {
            throw new InvalidOperationException("The extract system is already initialized.");
        }

        var previous = _initialization;
        _initialization = InitializationPhase.Query;
        try {
            var parentWorld = world.TryGetAddon<IWorldSource>(out var source)
                ? source.Source
                : world;
            _parentQuery = extractMatcher is null || extractMatcher == Matchers.Any
                ? parentWorld
                : parentWorld.Query(extractMatcher, cache: false);

            _extract = new ExtractQuery(_parentQuery);
        }
        finally {
            _initialization = previous;
        }
    }

    public override void Uninitialize(World world)
        => ReleaseQuery();

    private void ReleaseQuery()
    {
        var query = _parentQuery;
        _parentQuery = null!;
        _extract = null!;
        if (query is World.EntityQuery ownedQuery) ownedQuery.Dispose();
    }

    public sealed override void Execute(WorldContext context, IEntityQuery query)
        => Execute(context, query, _extract);

    public abstract void Execute(WorldContext context, IEntityQuery query, IEntityQuery extract);
}

public sealed class ExtractChannel<T> : IDisposable where T : unmanaged
{
    private readonly Channel<T> _channel;
    private bool _disposed;

    public ChannelWriter<T> Writer => _channel.Writer;
    public ChannelReader<T> Reader => _channel.Reader;

    public ExtractChannel(int capacity = 256)
    {
        _channel = Channel.CreateBounded<T>(new BoundedChannelOptions(capacity) {
            FullMode = BoundedChannelFullMode.Wait
        });
    }

    public bool TryWrite(T item) => _channel.Writer.TryWrite(item);
    public bool TryRead(out T item) => _channel.Reader.TryRead(out item);

    public int Drain(Action<T> handler)
    {
        var count = 0;
        while (_channel.Reader.TryRead(out var item)) {
            handler(item);
            count++;
        }
        return count;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _channel.Writer.TryComplete();
    }
}
