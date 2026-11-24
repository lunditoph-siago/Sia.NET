using System.Collections.Immutable;

namespace Sia;

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
        var index = 0;
        foreach (var entry in Entries) {
            if (entry.Id == id) {
                return new(Entries.RemoveAt(index));
            }
            index++;
        }
        return this;
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
        for (var index = 0; index < entries.Count; index++) {
            var entry = entries[index];
            if (entry.Id == id) {
                if (!ReferenceEquals(entries, Entries)) {
                    return ConfigureRemaining(entries, index, id, configure);
                }
                entries = entries.SetItem(index, entry with { Descriptor = configure(entry.Descriptor) });
            }
        }
        return ReferenceEquals(entries, Entries) ? this : new(entries);
    }

    private static SystemChain ConfigureRemaining(
        ImmutableList<Entry> entries,
        int startIndex,
        SystemId id,
        Func<SystemDescriptor, SystemDescriptor> configure)
    {
        var updated = entries.ToBuilder();
        for (var index = startIndex; index < entries.Count; index++) {
            var entry = entries[index];
            if (entry.Id == id) {
                updated[index] = entry with { Descriptor = configure(entry.Descriptor) };
            }
        }
        return new(updated.ToImmutable());
    }

    public ExecutionPlan Plan()
        => Planner.Plan(Entries);

    public SystemStage CreateStage(World world)
        => new(world, Plan());
}
