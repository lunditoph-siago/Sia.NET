using CommunityToolkit.HighPerformance;

namespace Sia;

public delegate void QueryHandler(IEntityQuery query, IRunner runner, RunnerBarrier? barrier);

public class FSystem(QueryHandler queryHandler,
    IEntityMatcher matcher, IEventUnion? trigger = null, IEventUnion? filter = null)
    : SystemBase(matcher, trigger, filter)
{
    private readonly QueryHandler _queryHandler = queryHandler;

    public override void Execute(World world, Scheduler scheduler, IEntityQuery query)
        => _queryHandler(query, CurrentThreadRunner.Instance, null);

    public FSystem WithMatcher(IEntityMatcher matcher)
        => new(_queryHandler, Matcher!.And(matcher), Trigger, Filter);

    public FSystemWithRunner WithRunner(IRunner runner)
        => new(_queryHandler, runner, Matcher, Trigger, Filter);
    
    public static FSystem<T1> From<T1>(ComponentHandler<T1> handler,
        IEntityMatcher? matcher = null, IEventUnion? trigger = null, IEventUnion? filter = null)
        => new(handler, matcher, trigger, filter);

    public static FSystem<T1, T2> From<T1, T2>(ComponentHandler<T1, T2> handler,
        IEntityMatcher? matcher = null, IEventUnion? trigger = null, IEventUnion? filter = null)
        => new(handler, matcher, trigger, filter);

    public static FSystem<T1, T2, T3> From<T1, T2, T3>(ComponentHandler<T1, T2, T3> handler,
        IEntityMatcher? matcher = null, IEventUnion? trigger = null, IEventUnion? filter = null)
        => new(handler, matcher, trigger, filter);

    public static FSystem<T1, T2, T3, T4> From<T1, T2, T3, T4>(ComponentHandler<T1, T2, T3, T4> handler,
        IEntityMatcher? matcher = null, IEventUnion? trigger = null, IEventUnion? filter = null)
        => new(handler, matcher, trigger, filter);

    public static FSystem<T1, T2, T3, T4, T5> From<T1, T2, T3, T4, T5>(ComponentHandler<T1, T2, T3, T4, T5> handler,
        IEntityMatcher? matcher = null, IEventUnion? trigger = null, IEventUnion? filter = null)
        => new(handler, matcher, trigger, filter);

    public static FSystem<T1, T2, T3, T4, T5, T6> From<T1, T2, T3, T4, T5, T6>(ComponentHandler<T1, T2, T3, T4, T5, T6> handler,
        IEntityMatcher? matcher = null, IEventUnion? trigger = null, IEventUnion? filter = null)
        => new(handler, matcher, trigger, filter);
}

public class FSystemWithRunner(QueryHandler queryHandler, IRunner? runner = null,
    IEntityMatcher? matcher = null, IEventUnion? trigger = null, IEventUnion? filter = null)
    : SystemBase(matcher, trigger, filter)
{
    public IRunner Runner { get; } = runner ?? CurrentThreadRunner.Instance;

    private readonly QueryHandler _queryHandler = queryHandler;

    public override void Execute(World world, Scheduler scheduler, IEntityQuery query)
        => _queryHandler(query, Runner, null);

    public FSystemWithBarrier WithBarrier()
        => new(_queryHandler, Runner, Matcher, Trigger, Filter);
}

public class FSystemWithBarrier(QueryHandler queryHandler, IRunner? runner = null,
    IEntityMatcher? matcher = null, IEventUnion? trigger = null, IEventUnion? filter = null)
    : SystemBase(matcher, trigger, filter)
{
    public IRunner Runner { get; } = runner ?? CurrentThreadRunner.Instance;

    public override void Execute(World world, Scheduler scheduler, IEntityQuery query)
    {
        var barrier = RunnerBarrier.Get();
        queryHandler(query, Runner, barrier);
        barrier.WaitAndReturn();
    }
}

public class FSystem<T1>(ComponentHandler<T1> handler,
    IEntityMatcher? matcher = null, IEventUnion? trigger = null, IEventUnion? filter = null)
    : FSystem(
        (query, runner, barrier) => query.ForSlice(handler, runner, barrier),
        matcher ?? Matchers.Of<T1>(), trigger, filter);

public class FSystem<T1, T2>(ComponentHandler<T1, T2> handler,
    IEntityMatcher? matcher = null, IEventUnion? trigger = null, IEventUnion? filter = null)
    : FSystem(
        (query, runner, barrier) => query.ForSlice(handler, runner, barrier),
        matcher ?? Matchers.Of<T1, T2>(), trigger, filter);

public class FSystem<T1, T2, T3>(ComponentHandler<T1, T2, T3> handler,
    IEntityMatcher? matcher = null, IEventUnion? trigger = null, IEventUnion? filter = null)
    : FSystem(
        (query, runner, barrier) => query.ForSlice(handler, runner, barrier),
        matcher ?? Matchers.Of<T1, T2, T3>(), trigger, filter);

public class FSystem<T1, T2, T3, T4>(ComponentHandler<T1, T2, T3, T4> handler,
    IEntityMatcher? matcher = null, IEventUnion? trigger = null, IEventUnion? filter = null)
    : FSystem(
        (query, runner, barrier) => query.ForSlice(handler, runner, barrier),
        matcher ?? Matchers.Of<T1, T2, T3, T4>(), trigger, filter);

public class FSystem<T1, T2, T3, T4, T5>(ComponentHandler<T1, T2, T3, T4, T5> handler,
    IEntityMatcher? matcher = null, IEventUnion? trigger = null, IEventUnion? filter = null)
    : FSystem(
        (query, runner, barrier) => query.ForSlice(handler, runner, barrier),
        matcher ?? Matchers.Of<T1, T2, T3, T4, T5>(), trigger, filter);

public class FSystem<T1, T2, T3, T4, T5, T6>(ComponentHandler<T1, T2, T3, T4, T5, T6> handler,
    IEntityMatcher? matcher = null, IEventUnion? trigger = null, IEventUnion? filter = null)
    : FSystem(
        (query, runner, barrier) => query.ForSlice(handler, runner, barrier),
        matcher ?? Matchers.Of<T1, T2, T3, T4, T5, T6>(), trigger, filter);
    
public static class FSystemSystemChainExtensions
{
    public static SystemChain Add<T1>(this SystemChain chain, ComponentHandler<T1> handler,
        IEntityMatcher? matcher = null, IEventUnion? trigger = null, IEventUnion? filter = null)
        => chain.Add(() => FSystem.From(handler, matcher, trigger, filter));

    public static SystemChain Add<T1, T2>(this SystemChain chain, ComponentHandler<T1, T2> handler,
        IEntityMatcher? matcher = null, IEventUnion? trigger = null, IEventUnion? filter = null)
        => chain.Add(() => FSystem.From(handler, matcher, trigger, filter));

    public static SystemChain Add<T1, T2, T3>(this SystemChain chain, ComponentHandler<T1, T2, T3> handler,
        IEntityMatcher? matcher = null, IEventUnion? trigger = null, IEventUnion? filter = null)
        => chain.Add(() => FSystem.From(handler, matcher, trigger, filter));

    public static SystemChain Add<T1, T2, T3, T4>(this SystemChain chain, ComponentHandler<T1, T2, T3, T4> handler,
        IEntityMatcher? matcher = null, IEventUnion? trigger = null, IEventUnion? filter = null)
        => chain.Add(() => FSystem.From(handler, matcher, trigger, filter));

    public static SystemChain Add<T1, T2, T3, T4, T5>(this SystemChain chain, ComponentHandler<T1, T2, T3, T4, T5> handler,
        IEntityMatcher? matcher = null, IEventUnion? trigger = null, IEventUnion? filter = null)
        => chain.Add(() => FSystem.From(handler, matcher, trigger, filter));

    public static SystemChain Add<T1, T2, T3, T4, T5, T6>(this SystemChain chain, ComponentHandler<T1, T2, T3, T4, T5, T6> handler,
        IEntityMatcher? matcher = null, IEventUnion? trigger = null, IEventUnion? filter = null)
        => chain.Add(() => FSystem.From(handler, matcher, trigger, filter));
}