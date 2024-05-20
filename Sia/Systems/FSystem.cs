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
    
    public static FSystem<T1> From<T1>(ComponentHandlerWithEntity<T1> handler,
        IEntityMatcher? matcher = null, IEventUnion? trigger = null, IEventUnion? filter = null)
        => new(handler, matcher, trigger, filter);

    public static FSystem<T1> From<T1>(ComponentHandler<T1> handler,
        IEntityMatcher? matcher = null, IEventUnion? trigger = null, IEventUnion? filter = null)
        => new((in EntityRef _, ref T1 c1) => handler(ref c1),
            matcher, trigger, filter);

    public static FSystem<T1, T2> From<T1, T2>(ComponentHandlerWithEntity<T1, T2> handler,
        IEntityMatcher? matcher = null, IEventUnion? trigger = null, IEventUnion? filter = null)
        => new(handler, matcher, trigger, filter);

    public static FSystem<T1, T2> From<T1, T2>(ComponentHandler<T1, T2> handler,
        IEntityMatcher? matcher = null, IEventUnion? trigger = null, IEventUnion? filter = null)
        => new((in EntityRef _, ref T1 c1, ref T2 c2) => handler(ref c1, ref c2),
            matcher, trigger, filter);

    public static FSystem<T1, T2, T3> From<T1, T2, T3>(ComponentHandlerWithEntity<T1, T2, T3> handler,
        IEntityMatcher? matcher = null, IEventUnion? trigger = null, IEventUnion? filter = null)
        => new(handler, matcher, trigger, filter);

    public static FSystem<T1, T2, T3> From<T1, T2, T3>(ComponentHandler<T1, T2, T3> handler,
        IEntityMatcher? matcher = null, IEventUnion? trigger = null, IEventUnion? filter = null)
        => new((in EntityRef _, ref T1 c1, ref T2 c2, ref T3 c3) => handler(ref c1, ref c2, ref c3),
            matcher, trigger, filter);

    public static FSystem<T1, T2, T3, T4> From<T1, T2, T3, T4>(ComponentHandlerWithEntity<T1, T2, T3, T4> handler,
        IEntityMatcher? matcher = null, IEventUnion? trigger = null, IEventUnion? filter = null)
        => new(handler, matcher, trigger, filter);

    public static FSystem<T1, T2, T3, T4> From<T1, T2, T3, T4>(ComponentHandler<T1, T2, T3, T4> handler,
        IEntityMatcher? matcher = null, IEventUnion? trigger = null, IEventUnion? filter = null)
        => new((in EntityRef _, ref T1 c1, ref T2 c2, ref T3 c3, ref T4 c4) => handler(ref c1, ref c2, ref c3, ref c4),
            matcher, trigger, filter);

    public static FSystem<T1, T2, T3, T4, T5> From<T1, T2, T3, T4, T5>(ComponentHandlerWithEntity<T1, T2, T3, T4, T5> handler,
        IEntityMatcher? matcher = null, IEventUnion? trigger = null, IEventUnion? filter = null)
        => new(handler, matcher, trigger, filter);

    public static FSystem<T1, T2, T3, T4, T5> From<T1, T2, T3, T4, T5>(ComponentHandler<T1, T2, T3, T4, T5> handler,
        IEntityMatcher? matcher = null, IEventUnion? trigger = null, IEventUnion? filter = null)
        => new((in EntityRef _, ref T1 c1, ref T2 c2, ref T3 c3, ref T4 c4, ref T5 c5) => handler(ref c1, ref c2, ref c3, ref c4, ref c5),
            matcher, trigger, filter);

    public static FSystem<T1, T2, T3, T4, T5, T6> From<T1, T2, T3, T4, T5, T6>(ComponentHandlerWithEntity<T1, T2, T3, T4, T5, T6> handler,
        IEntityMatcher? matcher = null, IEventUnion? trigger = null, IEventUnion? filter = null)
        => new(handler, matcher, trigger, filter);

    public static FSystem<T1, T2, T3, T4, T5, T6> From<T1, T2, T3, T4, T5, T6>(ComponentHandler<T1, T2, T3, T4, T5, T6> handler,
        IEntityMatcher? matcher = null, IEventUnion? trigger = null, IEventUnion? filter = null)
        => new((in EntityRef _, ref T1 c1, ref T2 c2, ref T3 c3, ref T4 c4, ref T5 c5, ref T6 c6) => handler(ref c1, ref c2, ref c3, ref c4, ref c5, ref c6),
            matcher, trigger, filter);
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

public class FSystem<T1>(ComponentHandlerWithEntity<T1> handler,
    IEntityMatcher? matcher = null, IEventUnion? trigger = null, IEventUnion? filter = null)
    : FSystem(
        (query, runner, barrier) => query.ForSlice(handler, runner, barrier),
        matcher ?? Matchers.Of<T1>(), trigger, filter);

public class FSystem<T1, T2>(ComponentHandlerWithEntity<T1, T2> handler,
    IEntityMatcher? matcher = null, IEventUnion? trigger = null, IEventUnion? filter = null)
    : FSystem(
        (query, runner, barrier) => query.ForSlice(handler, runner, barrier),
        matcher ?? Matchers.Of<T1, T2>(), trigger, filter);

public class FSystem<T1, T2, T3>(ComponentHandlerWithEntity<T1, T2, T3> handler,
    IEntityMatcher? matcher = null, IEventUnion? trigger = null, IEventUnion? filter = null)
    : FSystem(
        (query, runner, barrier) => query.ForSlice(handler, runner, barrier),
        matcher ?? Matchers.Of<T1, T2, T3>(), trigger, filter);

public class FSystem<T1, T2, T3, T4>(ComponentHandlerWithEntity<T1, T2, T3, T4> handler,
    IEntityMatcher? matcher = null, IEventUnion? trigger = null, IEventUnion? filter = null)
    : FSystem(
        (query, runner, barrier) => query.ForSlice(handler, runner, barrier),
        matcher ?? Matchers.Of<T1, T2, T3, T4>(), trigger, filter);

public class FSystem<T1, T2, T3, T4, T5>(ComponentHandlerWithEntity<T1, T2, T3, T4, T5> handler,
    IEntityMatcher? matcher = null, IEventUnion? trigger = null, IEventUnion? filter = null)
    : FSystem(
        (query, runner, barrier) => query.ForSlice(handler, runner, barrier),
        matcher ?? Matchers.Of<T1, T2, T3, T4, T5>(), trigger, filter);

public class FSystem<T1, T2, T3, T4, T5, T6>(ComponentHandlerWithEntity<T1, T2, T3, T4, T5, T6> handler,
    IEntityMatcher? matcher = null, IEventUnion? trigger = null, IEventUnion? filter = null)
    : FSystem(
        (query, runner, barrier) => query.ForSlice(handler, runner, barrier),
        matcher ?? Matchers.Of<T1, T2, T3, T4, T5, T6>(), trigger, filter);
    
public static class FSystemSystemChainExtensions
{
    public static SystemChain Add<T1>(this SystemChain chain, ComponentHandlerWithEntity<T1> handler,
        IEntityMatcher? matcher = null, IEventUnion? trigger = null, IEventUnion? filter = null)
        => chain.Add(() => FSystem.From(handler, matcher, trigger, filter));

    public static SystemChain Add<T1>(this SystemChain chain, ComponentHandler<T1> handler,
        IEntityMatcher? matcher = null, IEventUnion? trigger = null, IEventUnion? filter = null)
        => chain.Add(() => FSystem.From(handler, matcher, trigger, filter));

    public static SystemChain Add<T1, T2>(this SystemChain chain, ComponentHandlerWithEntity<T1, T2> handler,
        IEntityMatcher? matcher = null, IEventUnion? trigger = null, IEventUnion? filter = null)
        => chain.Add(() => FSystem.From(handler, matcher, trigger, filter));

    public static SystemChain Add<T1, T2>(this SystemChain chain, ComponentHandler<T1, T2> handler,
        IEntityMatcher? matcher = null, IEventUnion? trigger = null, IEventUnion? filter = null)
        => chain.Add(() => FSystem.From(handler, matcher, trigger, filter));

    public static SystemChain Add<T1, T2, T3>(this SystemChain chain, ComponentHandlerWithEntity<T1, T2, T3> handler,
        IEntityMatcher? matcher = null, IEventUnion? trigger = null, IEventUnion? filter = null)
        => chain.Add(() => FSystem.From(handler, matcher, trigger, filter));

    public static SystemChain Add<T1, T2, T3>(this SystemChain chain, ComponentHandler<T1, T2, T3> handler,
        IEntityMatcher? matcher = null, IEventUnion? trigger = null, IEventUnion? filter = null)
        => chain.Add(() => FSystem.From(handler, matcher, trigger, filter));

    public static SystemChain Add<T1, T2, T3, T4>(this SystemChain chain, ComponentHandlerWithEntity<T1, T2, T3, T4> handler,
        IEntityMatcher? matcher = null, IEventUnion? trigger = null, IEventUnion? filter = null)
        => chain.Add(() => FSystem.From(handler, matcher, trigger, filter));

    public static SystemChain Add<T1, T2, T3, T4>(this SystemChain chain, ComponentHandler<T1, T2, T3, T4> handler,
        IEntityMatcher? matcher = null, IEventUnion? trigger = null, IEventUnion? filter = null)
        => chain.Add(() => FSystem.From(handler, matcher, trigger, filter));

    public static SystemChain Add<T1, T2, T3, T4, T5>(this SystemChain chain, ComponentHandlerWithEntity<T1, T2, T3, T4, T5> handler,
        IEntityMatcher? matcher = null, IEventUnion? trigger = null, IEventUnion? filter = null)
        => chain.Add(() => FSystem.From(handler, matcher, trigger, filter));

    public static SystemChain Add<T1, T2, T3, T4, T5>(this SystemChain chain, ComponentHandler<T1, T2, T3, T4, T5> handler,
        IEntityMatcher? matcher = null, IEventUnion? trigger = null, IEventUnion? filter = null)
        => chain.Add(() => FSystem.From(handler, matcher, trigger, filter));

    public static SystemChain Add<T1, T2, T3, T4, T5, T6>(this SystemChain chain, ComponentHandlerWithEntity<T1, T2, T3, T4, T5, T6> handler,
        IEntityMatcher? matcher = null, IEventUnion? trigger = null, IEventUnion? filter = null)
        => chain.Add(() => FSystem.From(handler, matcher, trigger, filter));

    public static SystemChain Add<T1, T2, T3, T4, T5, T6>(this SystemChain chain, ComponentHandler<T1, T2, T3, T4, T5, T6> handler,
        IEntityMatcher? matcher = null, IEventUnion? trigger = null, IEventUnion? filter = null)
        => chain.Add(() => FSystem.From(handler, matcher, trigger, filter));
}