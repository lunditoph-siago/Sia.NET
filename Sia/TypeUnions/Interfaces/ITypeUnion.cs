namespace Sia;

using System.Collections.Immutable;

public interface ITypeUnion
{
    static virtual ImmutableArray<Type> Types { get; } = ImmutableArray<Type>.Empty;
    static virtual int Hash { get; } = 0;

    ImmutableArray<Type> ProxyTypes { get; }
}