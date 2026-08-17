namespace Sia;

public abstract class SnapshotExtractSystem<TExtract> : SystemBase
    where TExtract : unmanaged
{
    private IEntityQuery? _extractQuery;
    private bool _ownsQuery;
    private WorldSnapshot<TExtract> _snapshot = WorldSnapshot<TExtract>.Empty;
    private long _version;
    private World _extractWorld = null!;

    public ReadOnlySpan<TExtract> Data
        => Volatile.Read(ref _snapshot).Data;

    protected abstract IEntityMatcher ExtractMatcher { get; }
    protected abstract TExtract Extract(Entity entity);
    protected virtual void Render(ReadOnlySpan<TExtract> data) { }

    protected SnapshotExtractSystem(SystemChain? children = null)
        : base(Matchers.Any, children: children) { }

    public override void Initialize(World world)
    {
        _extractWorld = world.TryGetAddon<SubWorldContext>(out var ctx)
            ? ctx.Parent
            : world;
    }

    public override void Uninitialize(World world)
    {
        if (_ownsQuery && _extractQuery is not null) {
            _extractQuery.Dispose();
            _ownsQuery = false;
        }
        _extractQuery = null;
        _extractWorld = null!;
    }

    public sealed override void Execute(World world, IEntityQuery query)
        => Render(Data);

    public void RunExtract()
    {
        if (_extractWorld is null) {
            throw new InvalidOperationException(
                "The snapshot extract system has not been initialized.");
        }

        if (_extractQuery is null) {
            _ownsQuery = ExtractMatcher != Matchers.Any;
            _extractQuery = _ownsQuery
                ? _extractWorld.Query(ExtractMatcher)
                : _extractWorld;
        }

        var count = _extractQuery.Count;
        var data = new TExtract[count];
        var i = 0;
        _extractQuery.ForEach((EntityHandler)(entity => {
            data[i++] = Extract(entity);
        }));

        Volatile.Write(
            ref _snapshot,
            new WorldSnapshot<TExtract>(++_version, data, count));
    }
}
