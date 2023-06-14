namespace Sia;

using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

public class TypeUnionComparer : EqualityComparer<ITypeUnion>
{
    public static TypeUnionComparer Instance { get; } = new();

    public override bool Equals(ITypeUnion? x, ITypeUnion? y)
    {
        if (x == y) { return true; }
        if (x == null || y == null) { return false; }

        var xTypes = x.ProxyTypes;
        var yTypes = y.ProxyTypes;

        if (xTypes.Length != yTypes.Length) { return false; }

        int length = xTypes.Length;
        for (int i = 0; i != length; ++i) {
            if (xTypes[i] != yTypes[i]) {
                return false;
            }
        }

        return true;
    }

    public override int GetHashCode([DisallowNull] ITypeUnion obj)
        => obj.GetHashCode();
}

internal static class TypeUnionHelper
{
    public static ImmutableArray<Type> Sort(params (int, Type)[] types)
    {
        var dict = new SortedDictionary<int, Type>();
        foreach (var t in types) {
            dict.TryAdd(t.Item1, t.Item2);
        }
        return dict.Values.ToImmutableArray();
    }

    public static int CalculateHash(ImmutableArray<Type> types)
    {
        if (types.Length == 1) {
            return types[0].GetHashCode();
        }
        var hashCode = new HashCode();
        foreach (var t in types) {
            hashCode.Add(t);
        }
        return hashCode.ToHashCode();
    }
}

public class TypeUnion<T1> : ITypeUnion
{
    public static ImmutableArray<Type> Types { get; } = new[] { typeof(T1) }.ToImmutableArray();
    public static int Hash { get; } = typeof(T1).GetHashCode();

    public ImmutableArray<Type> ProxyTypes => Types;

    public override int GetHashCode() => Hash;
}

public class TypeUnion<T1, T2> : ITypeUnion
{
    public static ImmutableArray<Type> Types { get; } =
        TypeUnionHelper.Sort(
            (TypeIndexer<T1>.Index, typeof(T1)),
            (TypeIndexer<T2>.Index, typeof(T2)));

    public static int Hash { get; } =
        TypeUnionHelper.CalculateHash(Types);

    public ImmutableArray<Type> ProxyTypes => Types;

    public override int GetHashCode() => Hash;
}

public class TypeUnion<T1, T2, T3> : ITypeUnion
{
    public static ImmutableArray<Type> Types { get; } =
        TypeUnionHelper.Sort(
            (TypeIndexer<T1>.Index, typeof(T1)),
            (TypeIndexer<T2>.Index, typeof(T2)),
            (TypeIndexer<T3>.Index, typeof(T3)));

    public static int Hash { get; } =
        TypeUnionHelper.CalculateHash(Types);

    public ImmutableArray<Type> ProxyTypes => Types;

    public override int GetHashCode() => Hash;
}

public class TypeUnion<T1, T2, T3, T4> : ITypeUnion
{
    public static ImmutableArray<Type> Types { get; } =
        TypeUnionHelper.Sort(
            (TypeIndexer<T1>.Index, typeof(T1)),
            (TypeIndexer<T2>.Index, typeof(T2)),
            (TypeIndexer<T3>.Index, typeof(T3)),
            (TypeIndexer<T4>.Index, typeof(T4)));

    public static int Hash { get; } =
        TypeUnionHelper.CalculateHash(Types);

    public ImmutableArray<Type> ProxyTypes => Types;

    public override int GetHashCode() => Hash;
}

public class TypeUnion<T1, T2, T3, T4, T5> : ITypeUnion
{
    public static ImmutableArray<Type> Types { get; } =
        TypeUnionHelper.Sort(
            (TypeIndexer<T1>.Index, typeof(T1)),
            (TypeIndexer<T2>.Index, typeof(T2)),
            (TypeIndexer<T3>.Index, typeof(T3)),
            (TypeIndexer<T4>.Index, typeof(T4)),
            (TypeIndexer<T5>.Index, typeof(T5)));

    public static int Hash { get; } =
        TypeUnionHelper.CalculateHash(Types);

    public ImmutableArray<Type> ProxyTypes => Types;

    public override int GetHashCode() => Hash;
}

public class TypeUnion<T1, T2, T3, T4, T5, T6> : ITypeUnion
{
    public static ImmutableArray<Type> Types { get; } =
        TypeUnionHelper.Sort(
            (TypeIndexer<T1>.Index, typeof(T1)),
            (TypeIndexer<T2>.Index, typeof(T2)),
            (TypeIndexer<T3>.Index, typeof(T3)),
            (TypeIndexer<T4>.Index, typeof(T4)),
            (TypeIndexer<T5>.Index, typeof(T5)),
            (TypeIndexer<T6>.Index, typeof(T6)));

    public static int Hash { get; } =
        TypeUnionHelper.CalculateHash(Types);

    public ImmutableArray<Type> ProxyTypes => Types;

    public override int GetHashCode() => Hash;
}

public class TypeUnion<T1, T2, T3, T4, T5, T6, T7> : ITypeUnion
{
    public static ImmutableArray<Type> Types { get; } =
        TypeUnionHelper.Sort(
            (TypeIndexer<T1>.Index, typeof(T1)),
            (TypeIndexer<T2>.Index, typeof(T2)),
            (TypeIndexer<T3>.Index, typeof(T3)),
            (TypeIndexer<T4>.Index, typeof(T4)),
            (TypeIndexer<T5>.Index, typeof(T5)),
            (TypeIndexer<T6>.Index, typeof(T6)),
            (TypeIndexer<T7>.Index, typeof(T7)));

    public static int Hash { get; } =
        TypeUnionHelper.CalculateHash(Types);

    public ImmutableArray<Type> ProxyTypes => Types;

    public override int GetHashCode() => Hash;
}

public class TypeUnion<T1, T2, T3, T4, T5, T6, T7, T8> : ITypeUnion
{
    public static ImmutableArray<Type> Types { get; } =
        TypeUnionHelper.Sort(
            (TypeIndexer<T1>.Index, typeof(T1)),
            (TypeIndexer<T2>.Index, typeof(T2)),
            (TypeIndexer<T3>.Index, typeof(T3)),
            (TypeIndexer<T4>.Index, typeof(T4)),
            (TypeIndexer<T5>.Index, typeof(T5)),
            (TypeIndexer<T6>.Index, typeof(T6)),
            (TypeIndexer<T7>.Index, typeof(T7)),
            (TypeIndexer<T8>.Index, typeof(T8)));

    public static int Hash { get; } =
        TypeUnionHelper.CalculateHash(Types);

    public ImmutableArray<Type> ProxyTypes => Types;

    public override int GetHashCode() => Hash;
}

public class TypeUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9> : ITypeUnion
{
    public static ImmutableArray<Type> Types { get; } =
        TypeUnionHelper.Sort(
            (TypeIndexer<T1>.Index, typeof(T1)),
            (TypeIndexer<T2>.Index, typeof(T2)),
            (TypeIndexer<T3>.Index, typeof(T3)),
            (TypeIndexer<T4>.Index, typeof(T4)),
            (TypeIndexer<T5>.Index, typeof(T5)),
            (TypeIndexer<T6>.Index, typeof(T6)),
            (TypeIndexer<T7>.Index, typeof(T7)),
            (TypeIndexer<T8>.Index, typeof(T8)),
            (TypeIndexer<T9>.Index, typeof(T9)));

    public static int Hash { get; } =
        TypeUnionHelper.CalculateHash(Types);

    public ImmutableArray<Type> ProxyTypes => Types;

    public override int GetHashCode() => Hash;
}

public class TypeUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> : ITypeUnion
{
    public static ImmutableArray<Type> Types { get; } =
        TypeUnionHelper.Sort(
            (TypeIndexer<T1>.Index, typeof(T1)),
            (TypeIndexer<T2>.Index, typeof(T2)),
            (TypeIndexer<T3>.Index, typeof(T3)),
            (TypeIndexer<T4>.Index, typeof(T4)),
            (TypeIndexer<T5>.Index, typeof(T5)),
            (TypeIndexer<T6>.Index, typeof(T6)),
            (TypeIndexer<T7>.Index, typeof(T7)),
            (TypeIndexer<T8>.Index, typeof(T8)),
            (TypeIndexer<T9>.Index, typeof(T9)),
            (TypeIndexer<T10>.Index, typeof(T10)));

    public static int Hash { get; } =
        TypeUnionHelper.CalculateHash(Types);

    public ImmutableArray<Type> ProxyTypes => Types;

    public override int GetHashCode() => Hash;
}

public class TypeUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11> : ITypeUnion
{
    public static ImmutableArray<Type> Types { get; } =
        TypeUnionHelper.Sort(
            (TypeIndexer<T1>.Index, typeof(T1)),
            (TypeIndexer<T2>.Index, typeof(T2)),
            (TypeIndexer<T3>.Index, typeof(T3)),
            (TypeIndexer<T4>.Index, typeof(T4)),
            (TypeIndexer<T5>.Index, typeof(T5)),
            (TypeIndexer<T6>.Index, typeof(T6)),
            (TypeIndexer<T7>.Index, typeof(T7)),
            (TypeIndexer<T8>.Index, typeof(T8)),
            (TypeIndexer<T9>.Index, typeof(T9)),
            (TypeIndexer<T10>.Index, typeof(T10)),
            (TypeIndexer<T11>.Index, typeof(T11)));

    public static int Hash { get; } =
        TypeUnionHelper.CalculateHash(Types);

    public ImmutableArray<Type> ProxyTypes => Types;

    public override int GetHashCode() => Hash;
}

public class TypeUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12> : ITypeUnion
{
    public static ImmutableArray<Type> Types { get; } =
        TypeUnionHelper.Sort(
            (TypeIndexer<T1>.Index, typeof(T1)),
            (TypeIndexer<T2>.Index, typeof(T2)),
            (TypeIndexer<T3>.Index, typeof(T3)),
            (TypeIndexer<T4>.Index, typeof(T4)),
            (TypeIndexer<T5>.Index, typeof(T5)),
            (TypeIndexer<T6>.Index, typeof(T6)),
            (TypeIndexer<T7>.Index, typeof(T7)),
            (TypeIndexer<T8>.Index, typeof(T8)),
            (TypeIndexer<T9>.Index, typeof(T9)),
            (TypeIndexer<T10>.Index, typeof(T10)),
            (TypeIndexer<T11>.Index, typeof(T11)),
            (TypeIndexer<T12>.Index, typeof(T12)));

    public static int Hash { get; } =
        TypeUnionHelper.CalculateHash(Types);

    public ImmutableArray<Type> ProxyTypes => Types;

    public override int GetHashCode() => Hash;
}

public class TypeUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13> : ITypeUnion
{
    public static ImmutableArray<Type> Types { get; } =
        TypeUnionHelper.Sort(
            (TypeIndexer<T1>.Index, typeof(T1)),
            (TypeIndexer<T2>.Index, typeof(T2)),
            (TypeIndexer<T3>.Index, typeof(T3)),
            (TypeIndexer<T4>.Index, typeof(T4)),
            (TypeIndexer<T5>.Index, typeof(T5)),
            (TypeIndexer<T6>.Index, typeof(T6)),
            (TypeIndexer<T7>.Index, typeof(T7)),
            (TypeIndexer<T8>.Index, typeof(T8)),
            (TypeIndexer<T9>.Index, typeof(T9)),
            (TypeIndexer<T10>.Index, typeof(T10)),
            (TypeIndexer<T11>.Index, typeof(T11)),
            (TypeIndexer<T12>.Index, typeof(T12)),
            (TypeIndexer<T13>.Index, typeof(T13)));

    public static int Hash { get; } =
        TypeUnionHelper.CalculateHash(Types);

    public ImmutableArray<Type> ProxyTypes => Types;

    public override int GetHashCode() => Hash;
}

public class TypeUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14> : ITypeUnion
{
    public static ImmutableArray<Type> Types { get; } =
        TypeUnionHelper.Sort(
            (TypeIndexer<T1>.Index, typeof(T1)),
            (TypeIndexer<T2>.Index, typeof(T2)),
            (TypeIndexer<T3>.Index, typeof(T3)),
            (TypeIndexer<T4>.Index, typeof(T4)),
            (TypeIndexer<T5>.Index, typeof(T5)),
            (TypeIndexer<T6>.Index, typeof(T6)),
            (TypeIndexer<T7>.Index, typeof(T7)),
            (TypeIndexer<T8>.Index, typeof(T8)),
            (TypeIndexer<T9>.Index, typeof(T9)),
            (TypeIndexer<T10>.Index, typeof(T10)),
            (TypeIndexer<T11>.Index, typeof(T11)),
            (TypeIndexer<T12>.Index, typeof(T12)),
            (TypeIndexer<T13>.Index, typeof(T13)),
            (TypeIndexer<T14>.Index, typeof(T14)));

    public static int Hash { get; } =
        TypeUnionHelper.CalculateHash(Types);

    public ImmutableArray<Type> ProxyTypes => Types;

    public override int GetHashCode() => Hash;
}

public class TypeUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15> : ITypeUnion
{
    public static ImmutableArray<Type> Types { get; } =
        TypeUnionHelper.Sort(
            (TypeIndexer<T1>.Index, typeof(T1)),
            (TypeIndexer<T2>.Index, typeof(T2)),
            (TypeIndexer<T3>.Index, typeof(T3)),
            (TypeIndexer<T4>.Index, typeof(T4)),
            (TypeIndexer<T5>.Index, typeof(T5)),
            (TypeIndexer<T6>.Index, typeof(T6)),
            (TypeIndexer<T7>.Index, typeof(T7)),
            (TypeIndexer<T8>.Index, typeof(T8)),
            (TypeIndexer<T9>.Index, typeof(T9)),
            (TypeIndexer<T10>.Index, typeof(T10)),
            (TypeIndexer<T11>.Index, typeof(T11)),
            (TypeIndexer<T12>.Index, typeof(T12)),
            (TypeIndexer<T13>.Index, typeof(T13)),
            (TypeIndexer<T14>.Index, typeof(T14)),
            (TypeIndexer<T15>.Index, typeof(T15)));

    public static int Hash { get; } =
        TypeUnionHelper.CalculateHash(Types);

    public ImmutableArray<Type> ProxyTypes => Types;

    public override int GetHashCode() => Hash;
}

public class TypeUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16> : ITypeUnion
{
    public static ImmutableArray<Type> Types { get; } =
        TypeUnionHelper.Sort(
            (TypeIndexer<T1>.Index, typeof(T1)),
            (TypeIndexer<T2>.Index, typeof(T2)),
            (TypeIndexer<T3>.Index, typeof(T3)),
            (TypeIndexer<T4>.Index, typeof(T4)),
            (TypeIndexer<T5>.Index, typeof(T5)),
            (TypeIndexer<T6>.Index, typeof(T6)),
            (TypeIndexer<T7>.Index, typeof(T7)),
            (TypeIndexer<T8>.Index, typeof(T8)),
            (TypeIndexer<T9>.Index, typeof(T9)),
            (TypeIndexer<T10>.Index, typeof(T10)),
            (TypeIndexer<T11>.Index, typeof(T11)),
            (TypeIndexer<T12>.Index, typeof(T12)),
            (TypeIndexer<T13>.Index, typeof(T13)),
            (TypeIndexer<T14>.Index, typeof(T14)),
            (TypeIndexer<T15>.Index, typeof(T15)),
            (TypeIndexer<T16>.Index, typeof(T16)));

    public static int Hash { get; } =
        TypeUnionHelper.CalculateHash(Types);

    public ImmutableArray<Type> ProxyTypes => Types;

    public override int GetHashCode() => Hash;
}