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
    private World _parentWorld = null!;
    private IEntityQuery _parentQuery = null!;
    private ExtractQuery _extract = null!;
    private bool _ownsParentQuery;

    public override void Initialize(World world)
    {
        _parentWorld = world.TryGetAddon<SubWorldContext>(out var ctx)
            ? ctx.Parent
            : world;
        _ownsParentQuery = extractMatcher is not null && extractMatcher != Matchers.Any;

        if (_ownsParentQuery) {
            _parentQuery = _parentWorld.Query(extractMatcher);
        } else {
            _parentQuery = _parentWorld;
        }

        _extract = new ExtractQuery(_parentQuery);
    }

    public override void Uninitialize(World world)
    {
        if (_ownsParentQuery) {
            _parentQuery.Dispose();
            _ownsParentQuery = false;
        }
        _parentQuery = null!;
        _extract = null!;
        _parentWorld = null!;
    }

    public sealed override void Execute(World world, IEntityQuery query)
        => Execute(world, query, _extract);

    public abstract void Execute(World world, IEntityQuery query, IEntityQuery extract);
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
