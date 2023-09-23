namespace Sia;

using System.Collections.Immutable;

public interface ITypeUnion : IEquatable<ITypeUnion>
{
    ImmutableArray<Type> Types { get; }
    ImmutableSortedDictionary<int, Type> IndexTypeDictionary { get; }

    ITypeUnion Merge(ITypeUnion other);
}

public interface IStaticTypeUnion : ITypeUnion
{
    static virtual ImmutableArray<Type> StaticTypes { get; } = ImmutableArray<Type>.Empty;
    static virtual int StaticHash { get; } = 0;

    static virtual ImmutableSortedDictionary<int, Type> StatiIndexTypeDictionary { get; }
        = ImmutableSortedDictionary<int, Type>.Empty;
}