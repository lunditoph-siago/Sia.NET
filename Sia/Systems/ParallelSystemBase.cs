namespace Sia;

public abstract class ParallelSystemBase<C1>(
    IEntityMatcher? matcher = null, IEventUnion? trigger = null, IEventUnion? filter = null,
    SystemChain? children = null, IRunner? runner = null)
    : SystemBase(matcher ?? Matchers.Of<C1>(), trigger, filter, children)
{
    public IRunner Runner { get; } = runner ?? ParallelRunner.Default;

    public override void Execute(World world, IEntityQuery query)
    {
        var barrier = RunnerBarrier.Get();
        query.ForSlice<IRunner, C1>(HandleSlice, Runner, barrier);
        barrier.WaitAndReturn();
    }

    protected abstract void HandleSlice(ref C1 c1);
}

public abstract class ParallelSystemBase<C1, C2>(
    IEntityMatcher? matcher = null, IEventUnion? trigger = null, IEventUnion? filter = null,
    SystemChain? children = null, IRunner? runner = null)
    : SystemBase(matcher ?? Matchers.Of<C1, C2>(), trigger, filter, children)
{
    public IRunner Runner { get; } = runner ?? ParallelRunner.Default;

    public override void Execute(World world, IEntityQuery query)
    {
        var barrier = RunnerBarrier.Get();
        query.ForSlice<IRunner, C1, C2>(OnExecute, Runner, barrier);
        barrier.WaitAndReturn();
    }

    protected abstract void OnExecute(ref C1 c1, ref C2 c2);
}

public abstract class ParallelSystemBase<C1, C2, C3>(
    IEntityMatcher? matcher = null, IEventUnion? trigger = null, IEventUnion? filter = null,
    SystemChain? children = null, IRunner? runner = null)
    : SystemBase(matcher ?? Matchers.Of<C1, C2, C3>(), trigger, filter, children)
{
    public IRunner Runner { get; } = runner ?? ParallelRunner.Default;

    public override void Execute(World world, IEntityQuery query)
    {
        var barrier = RunnerBarrier.Get();
        query.ForSlice<IRunner, C1, C2, C3>(OnExecute, Runner, barrier);
        barrier.WaitAndReturn();
    }

    protected abstract void OnExecute(ref C1 c1, ref C2 c2, ref C3 c3);
}

public abstract class ParallelSystemBase<C1, C2, C3, C4>(
    IEntityMatcher? matcher = null, IEventUnion? trigger = null, IEventUnion? filter = null,
    SystemChain? children = null, IRunner? runner = null)
    : SystemBase(matcher ?? Matchers.Of<C1, C2, C3, C4>(), trigger, filter, children)
{
    public IRunner Runner { get; } = runner ?? ParallelRunner.Default;

    public override void Execute(World world, IEntityQuery query)
    {
        var barrier = RunnerBarrier.Get();
        query.ForSlice<IRunner, C1, C2, C3, C4>(OnExecute, Runner, barrier);
        barrier.WaitAndReturn();
    }

    protected abstract void OnExecute(ref C1 c1, ref C2 c2, ref C3 c3, ref C4 c4);
}

public abstract class ParallelSystemBase<C1, C2, C3, C4, C5>(
    IEntityMatcher? matcher = null, IEventUnion? trigger = null, IEventUnion? filter = null,
    SystemChain? children = null, IRunner? runner = null)
    : SystemBase(matcher ?? Matchers.Of<C1, C2, C3, C4, C5>(), trigger, filter, children)
{
    public IRunner Runner { get; } = runner ?? ParallelRunner.Default;

    public override void Execute(World world, IEntityQuery query)
    {
        var barrier = RunnerBarrier.Get();
        query.ForSlice<IRunner, C1, C2, C3, C4, C5>(OnExecute, Runner, barrier);
        barrier.WaitAndReturn();
    }

    protected abstract void OnExecute(ref C1 c1, ref C2 c2, ref C3 c3, ref C4 c4, ref C5 c5);
}

public abstract class ParallelSystemBase<C1, C2, C3, C4, C5, C6>(
    IEntityMatcher? matcher = null, IEventUnion? trigger = null, IEventUnion? filter = null,
    SystemChain? children = null, IRunner? runner = null)
    : SystemBase(matcher ?? Matchers.Of<C1, C2, C3, C4, C5>(), trigger, filter, children)
{
   public IRunner Runner { get; } = runner ?? ParallelRunner.Default;

    public override void Execute(World world, IEntityQuery query)
    {
        var barrier = RunnerBarrier.Get();
        query.ForSlice<IRunner, C1, C2, C3, C4, C5, C6>(OnExecute, Runner, barrier);
        barrier.WaitAndReturn();
    }

    protected abstract void OnExecute(ref C1 c1, ref C2 c2, ref C3 c3, ref C4 c4, ref C5 c5, ref C6 c6);
}