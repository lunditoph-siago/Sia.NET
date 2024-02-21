namespace Sia;

public abstract class ParallelSystemBase<C1>(
    SystemChain? children = null, IEntityMatcher? matcher = null,
    IEventUnion? trigger = null, IEventUnion? filter = null,
    IRunner? runner = null)
    : SystemBase(children, matcher ?? Matchers.Of<C1>(), trigger, filter)
{
    public IRunner Runner { get; } = runner ?? ParallelRunner.Default;

    public override void Execute(World world, Scheduler scheduler, IEntityQuery query)
        => query.ForEach<IRunner, C1>(OnExecute, Runner);

    protected abstract void OnExecute(ref C1 c1);
}

public abstract class ParallelSystemBase<C1, C2>(
    SystemChain? children = null, IEntityMatcher? matcher = null,
    IEventUnion? trigger = null, IEventUnion? filter = null,
    IRunner? runner = null)
    : SystemBase(children, matcher ?? Matchers.Of<C1, C2>(), trigger, filter)
{
    public IRunner Runner { get; } = runner ?? ParallelRunner.Default;

    public override void Execute(World world, Scheduler scheduler, IEntityQuery query)
        => query.ForEach<IRunner, C1, C2>(OnExecute, Runner);

    protected abstract void OnExecute(ref C1 c1, ref C2 c2);
}

public abstract class ParallelSystemBase<C1, C2, C3>(
    SystemChain? children = null, IEntityMatcher? matcher = null,
    IEventUnion? trigger = null, IEventUnion? filter = null,
    IRunner? runner = null)
    : SystemBase(children, matcher ?? Matchers.Of<C1, C2, C3>(), trigger, filter)
{
    public IRunner Runner { get; } = runner ?? ParallelRunner.Default;

    public override void Execute(World world, Scheduler scheduler, IEntityQuery query)
        => query.ForEach<IRunner, C1, C2, C3>(OnExecute, Runner);

    protected abstract void OnExecute(ref C1 c1, ref C2 c2, ref C3 c3);
}

public abstract class ParallelSystemBase<C1, C2, C3, C4>(
    SystemChain? children = null, IEntityMatcher? matcher = null,
    IEventUnion? trigger = null, IEventUnion? filter = null,
    IRunner? runner = null)
    : SystemBase(children, matcher ?? Matchers.Of<C1, C2, C3, C4>(), trigger, filter)
{
    public IRunner Runner { get; } = runner ?? ParallelRunner.Default;

    public override void Execute(World world, Scheduler scheduler, IEntityQuery query)
        => query.ForEach<IRunner, C1, C2, C3, C4>(OnExecute, Runner);

    protected abstract void OnExecute(ref C1 c1, ref C2 c2, ref C3 c3, ref C4 c4);
}

public abstract class ParallelSystemBase<C1, C2, C3, C4, C5>(
    SystemChain? children = null, IEntityMatcher? matcher = null,
    IEventUnion? trigger = null, IEventUnion? filter = null,
    IRunner? runner = null)
    : SystemBase(children, matcher ?? Matchers.Of<C1, C2, C3, C4, C5>(), trigger, filter)
{
    public IRunner Runner { get; } = runner ?? ParallelRunner.Default;

    public override void Execute(World world, Scheduler scheduler, IEntityQuery query)
        => query.ForEach<IRunner, C1, C2, C3, C4, C5>(OnExecute, Runner);

    protected abstract void OnExecute(ref C1 c1, ref C2 c2, ref C3 c3, ref C4 c4, ref C5 c5);
}

public abstract class ParallelSystemBase<C1, C2, C3, C4, C5, C6>(
    SystemChain? children = null, IEntityMatcher? matcher = null,
    IEventUnion? trigger = null, IEventUnion? filter = null,
    IRunner? runner = null)
    : SystemBase(children, matcher ?? Matchers.Of<C1, C2, C3, C4, C5>(), trigger, filter)
{
    public IRunner Runner { get; } = runner ?? ParallelRunner.Default;

    public override void Execute(World world, Scheduler scheduler, IEntityQuery query)
        => query.ForEach<IRunner, C1, C2, C3, C4, C5, C6>(OnExecute, Runner);

    protected abstract void OnExecute(ref C1 c1, ref C2 c2, ref C3 c3, ref C4 c4, ref C5 c5, ref C6 c6);
}