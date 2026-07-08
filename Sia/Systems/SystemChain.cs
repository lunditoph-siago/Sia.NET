namespace Sia;

using System.Collections.Immutable;

public sealed record SystemChain
{
    public readonly record struct Entry(
        SystemId Id,
        Func<ISystem> Creator,
        SystemDescriptor Descriptor);

    public static readonly SystemChain Empty = new([]);

    public ImmutableList<Entry> Entries { get; }

    private SystemChain(ImmutableList<Entry> entries)
    {
        Entries = entries;
    }

    public SystemChain Add<TSystem>()
        where TSystem : ISystem, new()
        => Add(
            SystemId.For<TSystem>(),
            static () => new TSystem(),
            SystemDescriptorProvider.GetOrDefault(typeof(TSystem)));

    public SystemChain Add<TSystem>(Func<TSystem> creator)
        where TSystem : ISystem
        => Add(
            SystemId.For<TSystem>(),
            () => creator(),
            SystemDescriptorProvider.GetOrDefault(typeof(TSystem)));

    public SystemChain Add<TSystem>(SystemId id, Func<TSystem> creator)
        where TSystem : ISystem
        => Add(id, () => creator(), SystemDescriptor.ForId(id));

    public SystemChain Add(SystemId id, Func<ISystem> creator)
        => Add(id, creator, SystemDescriptor.ForId(id));

    public SystemChain Add(
        SystemId id,
        Func<ISystem> creator,
        SystemDescriptor descriptor)
        => new(Entries.Add(new(id, creator, descriptor)));

    public SystemChain Concat(SystemChain chain)
        => new(Entries.AddRange(chain.Entries));

    public SystemChain Remove<TSystem>()
        where TSystem : ISystem
        => Remove(SystemId.For<TSystem>());

    public SystemChain Remove(SystemId id)
    {
        var index = Entries.FindIndex(entry => entry.Id == id);
        return index == -1 ? this : new(Entries.RemoveAt(index));
    }

    public SystemChain RemoveAll<TSystem>()
        where TSystem : ISystem
        => RemoveAll(SystemId.For<TSystem>());

    public SystemChain RemoveAll(SystemId id)
    {
        var entries = Entries.RemoveAll(entry => entry.Id == id);
        return ReferenceEquals(entries, Entries) ? this : new(entries);
    }

    public SystemChain Configure<TSystem>(
        Func<SystemDescriptor, SystemDescriptor> configure)
        where TSystem : ISystem
        => Configure(SystemId.For<TSystem>(), configure);

    public SystemChain Configure(
        SystemId id,
        Func<SystemDescriptor, SystemDescriptor> configure)
    {
        var entries = Entries;
        for (var i = 0; i < entries.Count; i++) {
            var entry = entries[i];
            if (entry.Id == id) {
                entries = entries.SetItem(
                    i, entry with { Descriptor = configure(entry.Descriptor) });
            }
        }
        return ReferenceEquals(entries, Entries) ? this : new(entries);
    }

    public ExecutionPlan Plan()
        => Planner.Plan(Entries);

    public SystemStage CreateStage(World world)
        => new(world, Plan());
}
