namespace Sia;

using System.Collections.Immutable;
using System.Runtime.CompilerServices;

public record SystemChain
{
    public readonly record struct Entry(
        SystemId Id,
        Type Type,
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
        => new(Entries.Add(new(
            SystemId.For<TSystem>(),
            typeof(TSystem),
            static () => new TSystem(),
            SystemDescriptorProvider.GetOrDefault(typeof(TSystem))
        )));

    public SystemChain Add<TSystem>(Func<TSystem> creator)
        where TSystem : ISystem
        => Add(
            SystemId.For<TSystem>(),
            typeof(TSystem),
            Unsafe.As<Func<ISystem>>(creator),
            SystemDescriptorProvider.GetOrDefault(typeof(TSystem)));

    public SystemChain Add<TSystem>(SystemId id, Func<TSystem> creator)
        where TSystem : ISystem
        => Add(id, typeof(TSystem), Unsafe.As<Func<ISystem>>(creator), SystemDescriptor.ForId(id));

    public SystemChain Add(SystemId id, Func<ISystem> creator)
        => Add(id, typeof(ISystem), creator, SystemDescriptor.ForId(id));

    public SystemChain Add(
        SystemId id,
        Type type,
        Func<ISystem> creator,
        SystemDescriptor descriptor)
        => new(Entries.Add(new(id, type, creator, descriptor)));

    public SystemChain Concat(SystemChain chain)
        => new(Entries.AddRange(chain.Entries));

    public SystemChain Remove<TSystem>()
        where TSystem : ISystem
    {
        var index = Entries.FindIndex(entry => entry.Type == typeof(TSystem));
        return index == -1 ? this : new(Entries.RemoveAt(index));
    }

    public SystemChain RemoveAll<TSystem>()
        where TSystem : ISystem
    {
        var entries = Entries.RemoveAll(entry => entry.Type == typeof(TSystem));
        return entries == Entries ? this : new(entries);
    }

    public SystemChain Configure(
        SystemId id,
        Func<SystemDescriptor, SystemDescriptor> configure)
    {
        var entries = Entries.Select(entry =>
            entry.Id == id
                ? entry with { Descriptor = configure(entry.Descriptor) }
                : entry);
        return new([..entries]);
    }

    public SystemChain Configure<TSystem>(
        Func<SystemDescriptor, SystemDescriptor> configure)
        where TSystem : ISystem
    {
        var id = SystemId.For<TSystem>();
        var entries = Entries.Select(entry =>
            entry.Id == id || entry.Type == typeof(TSystem)
                ? entry with { Descriptor = configure(entry.Descriptor) }
                : entry);
        return new([..entries]);
    }

    public SystemStage CreateStage(World world)
        => new(world, Entries.Select(entry => entry.Creator()));
}
