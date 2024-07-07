namespace Sia;

using System.Collections.Immutable;
using System.Runtime.CompilerServices;

public record SystemChain
{
    public readonly record struct Entry(
        Type Type, Func<ISystem> Creator);
    
    public static readonly SystemChain Empty = new([]);

    public ImmutableList<Entry> Entries { get; }

    private SystemChain(ImmutableList<Entry> entries)
    {
        Entries = entries;
    }

    public SystemChain Add<TSystem>()
        where TSystem : ISystem, new()
        => new(Entries.Add(new(
            typeof(TSystem),
            static () => new TSystem()
        )));

    public SystemChain Add<TSystem>(Func<TSystem> creator)
        where TSystem : ISystem
        => new(Entries.Add(new(
            typeof(TSystem),
            Unsafe.As<Func<ISystem>>(creator)
        )));

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

    public SystemStage CreateStage(World world)
        => new(world, Entries.Select(entry => entry.Creator()));
}