using System.Runtime.CompilerServices;

namespace Sia;

public abstract class SnapshotExtractSystem<TExtract> : SystemBase
    where TExtract : unmanaged
{
    private IEntityQuery? _extractQuery;
    private bool _ownsQuery;
    private int _count;
    private TExtract[] _data = [];
    private World _extractWorld = null!;

    public ReadOnlySpan<TExtract> Data => _data.AsSpan(0, _count);

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
        if (_extractQuery is null) {
            _extractQuery = _extractWorld.Query(ExtractMatcher);
            _ownsQuery = true;
        }
        _count = _extractQuery.Count;

        if (_data.Length < _count)
            _data = new TExtract[_count];

        var i = 0;
        _extractQuery.ForEach((EntityHandler)(entity => {
            _data[i++] = Extract(entity);
        }));
    }
}
