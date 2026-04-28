namespace Sia;

using System.Collections.Immutable;

public sealed record Schedule(
    ScheduleLabel Label,
    SystemChain Chain,
    ImmutableHashSet<ScheduleLabel> RunsBefore,
    ImmutableHashSet<ScheduleLabel> RunsAfter)
{
    public Schedule(ScheduleLabel label)
        : this(
            label,
            SystemChain.Empty,
            ImmutableHashSet<ScheduleLabel>.Empty,
            ImmutableHashSet<ScheduleLabel>.Empty) {}

    public Schedule Add<TSystem>()
        where TSystem : ISystem, new()
        => this with { Chain = Chain.Add<TSystem>() };

    public Schedule Add<TSystem>(Func<TSystem> creator)
        where TSystem : ISystem
        => this with { Chain = Chain.Add(creator) };

    public Schedule Configure<TSystem>(Func<SystemDescriptor, SystemDescriptor> configure)
        where TSystem : ISystem
        => this with { Chain = Chain.With<TSystem>(configure) };

    public Schedule Before(ScheduleLabel other)
        => this with { RunsBefore = RunsBefore.Add(other) };

    public Schedule After(ScheduleLabel other)
        => this with { RunsAfter = RunsAfter.Add(other) };

    internal SystemStage Build(World world) => Chain.CreateSortedStage(world);
}
