namespace Sia;

using System.Collections.Frozen;
using System.Collections.Immutable;

public interface ITypeUnion : IEquatable<ITypeUnion>
{
    ImmutableArray<Type> Types { get; }
    FrozenSet<Type> TypeSet { get; }

    ITypeUnion Merge(ITypeUnion other);
}

public interface IStaticTypeUnion : ITypeUnion
{
    static virtual ImmutableArray<Type> StaticTypes { get; } = ImmutableArray<Type>.Empty;
    static virtual int StaticHash { get; } = 0;

    static virtual FrozenSet<Type> StaticTypeSet { get; }
        = FrozenSet<Type>.Empty;
}