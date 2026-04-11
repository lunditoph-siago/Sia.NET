namespace Sia;

public sealed class ExtractQuery : IEntityQuery
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

    public override void Initialize(World world)
    {
        var ctx = world.GetAddon<SubWorldContext>();
        _parentWorld = ctx.Parent;

        _parentQuery = extractMatcher == null || extractMatcher == Matchers.Any
            ? _parentWorld
            : _parentWorld.Query(extractMatcher);

        _extract = new ExtractQuery(_parentQuery);
    }

    public override void Uninitialize(World world)
    {
        if (_parentQuery is not World) {
            _parentQuery.Dispose();
        }
        _parentQuery = null!;
        _extract = null!;
        _parentWorld = null!;
    }

    public sealed override void Execute(World world, IEntityQuery query)
        => Execute(world, query, _extract);

    public abstract void Execute(World world, IEntityQuery query, IEntityQuery extract);
}
