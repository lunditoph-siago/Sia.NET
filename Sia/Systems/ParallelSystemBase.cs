namespace Sia;

public abstract class ParallelSystemBase<C1>(
    IEntityMatcher? matcher = null, IEventUnion? trigger = null, IEventUnion? filter = null,
    SystemChain? children = null, IRunner? runner = null)
    : SystemBase(matcher ?? Matchers.Of<C1>(), trigger, filter, children)
{
    public IRunner Runner { get; } = runner ?? ParallelRunner.Default;

    public override void Execute(WorldContext context, IEntityQuery query)
    {
        var barrier = RunnerBarrier.Get();
        query.ForSlice<IRunner, WorldContext, C1>(context, HandleSlice, Runner, barrier);
        barrier.WaitAndReturn();
    }

    protected abstract void HandleSlice(in WorldContext context, ref C1 c1);
}

public abstract class ParallelSystemBase<C1, C2>(
    IEntityMatcher? matcher = null, IEventUnion? trigger = null, IEventUnion? filter = null,
    SystemChain? children = null, IRunner? runner = null)
    : SystemBase(matcher ?? Matchers.Of<C1, C2>(), trigger, filter, children)
{
    public IRunner Runner { get; } = runner ?? ParallelRunner.Default;

    public override void Execute(WorldContext context, IEntityQuery query)
    {
        var barrier = RunnerBarrier.Get();
        query.ForSlice<IRunner, WorldContext, C1, C2>(
            context, HandleSlice, Runner, barrier);
        barrier.WaitAndReturn();
    }

    protected abstract void HandleSlice(in WorldContext context, ref C1 c1, ref C2 c2);
}

public abstract class ParallelSystemBase<C1, C2, C3>(
    IEntityMatcher? matcher = null, IEventUnion? trigger = null, IEventUnion? filter = null,
    SystemChain? children = null, IRunner? runner = null)
    : SystemBase(matcher ?? Matchers.Of<C1, C2, C3>(), trigger, filter, children)
{
    public IRunner Runner { get; } = runner ?? ParallelRunner.Default;

    public override void Execute(WorldContext context, IEntityQuery query)
    {
        var barrier = RunnerBarrier.Get();
        query.ForSlice<IRunner, WorldContext, C1, C2, C3>(
            context, HandleSlice, Runner, barrier);
        barrier.WaitAndReturn();
    }

    protected abstract void HandleSlice(in WorldContext context, ref C1 c1, ref C2 c2, ref C3 c3);
}

public abstract class ParallelSystemBase<C1, C2, C3, C4>(
    IEntityMatcher? matcher = null, IEventUnion? trigger = null, IEventUnion? filter = null,
    SystemChain? children = null, IRunner? runner = null)
    : SystemBase(matcher ?? Matchers.Of<C1, C2, C3, C4>(), trigger, filter, children)
{
    public IRunner Runner { get; } = runner ?? ParallelRunner.Default;

    public override void Execute(WorldContext context, IEntityQuery query)
    {
        var barrier = RunnerBarrier.Get();
        query.ForSlice<IRunner, WorldContext, C1, C2, C3, C4>(
            context, HandleSlice, Runner, barrier);
        barrier.WaitAndReturn();
    }

    protected abstract void HandleSlice(in WorldContext context, ref C1 c1, ref C2 c2, ref C3 c3, ref C4 c4);
}

public abstract class ParallelSystemBase<C1, C2, C3, C4, C5>(
    IEntityMatcher? matcher = null, IEventUnion? trigger = null, IEventUnion? filter = null,
    SystemChain? children = null, IRunner? runner = null)
    : SystemBase(matcher ?? Matchers.Of<C1, C2, C3, C4, C5>(), trigger, filter, children)
{
    public IRunner Runner { get; } = runner ?? ParallelRunner.Default;

    public override void Execute(WorldContext context, IEntityQuery query)
    {
        var barrier = RunnerBarrier.Get();
        query.ForSlice<IRunner, WorldContext, C1, C2, C3, C4, C5>(
            context, HandleSlice, Runner, barrier);
        barrier.WaitAndReturn();
    }

    protected abstract void HandleSlice(in WorldContext context, ref C1 c1, ref C2 c2, ref C3 c3, ref C4 c4, ref C5 c5);
}

public abstract class ParallelSystemBase<C1, C2, C3, C4, C5, C6>(
    IEntityMatcher? matcher = null, IEventUnion? trigger = null, IEventUnion? filter = null,
    SystemChain? children = null, IRunner? runner = null)
    : SystemBase(matcher ?? Matchers.Of<C1, C2, C3, C4, C5, C6>(), trigger, filter, children)
{
    public IRunner Runner { get; } = runner ?? ParallelRunner.Default;

    public override void Execute(WorldContext context, IEntityQuery query)
    {
        var barrier = RunnerBarrier.Get();
        query.ForSlice<IRunner, WorldContext, C1, C2, C3, C4, C5, C6>(
            context, HandleSlice, Runner, barrier);
        barrier.WaitAndReturn();
    }

    protected abstract void HandleSlice(in WorldContext context, ref C1 c1, ref C2 c2, ref C3 c3, ref C4 c4, ref C5 c5, ref C6 c6);
}
