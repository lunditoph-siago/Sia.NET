namespace Sia;

using System.Collections.Immutable;

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

public abstract class TypeUnionBase : ITypeUnion
{
    public abstract ImmutableArray<Type> ProxyTypes { get; }

    public bool Equals(ITypeUnion? other)
    {
        if (this == other) { return true; }
        if (other == null) { return false; }

        var typesA = ProxyTypes;
        var typesB = other.ProxyTypes;

        if (typesA.Length != typesB.Length) { return false; }

        int length = typesA.Length;
        for (int i = 0; i != length; ++i) {
            if (typesA[i] != typesB[i]) {
                return false;
            }
        }

        return true;
    }
}

public class TypeUnion<T1> : TypeUnionBase
{
    public static ImmutableArray<Type> Types { get; } = new[] { typeof(T1) }.ToImmutableArray();
    public static int Hash { get; } = typeof(T1).GetHashCode();

    public override ImmutableArray<Type> ProxyTypes => Types;

    public override int GetHashCode() => Hash;
}

public class TypeUnion<T1, T2> : TypeUnionBase
{
    public static ImmutableArray<Type> Types { get; } =
        TypeUnionHelper.Sort(
            (TypeIndexer<T1>.Index, typeof(T1)),
            (TypeIndexer<T2>.Index, typeof(T2)));

    public static int Hash { get; } =
        TypeUnionHelper.CalculateHash(Types);

    public override ImmutableArray<Type> ProxyTypes => Types;

    public override int GetHashCode() => Hash;
}

public class TypeUnion<T1, T2, T3> : TypeUnionBase
{
    public static ImmutableArray<Type> Types { get; } =
        TypeUnionHelper.Sort(
            (TypeIndexer<T1>.Index, typeof(T1)),
            (TypeIndexer<T2>.Index, typeof(T2)),
            (TypeIndexer<T3>.Index, typeof(T3)));

    public static int Hash { get; } =
        TypeUnionHelper.CalculateHash(Types);

    public override ImmutableArray<Type> ProxyTypes => Types;

    public override int GetHashCode() => Hash;
}

public class TypeUnion<T1, T2, T3, T4> : TypeUnionBase
{
    public static ImmutableArray<Type> Types { get; } =
        TypeUnionHelper.Sort(
            (TypeIndexer<T1>.Index, typeof(T1)),
            (TypeIndexer<T2>.Index, typeof(T2)),
            (TypeIndexer<T3>.Index, typeof(T3)),
            (TypeIndexer<T4>.Index, typeof(T4)));

    public static int Hash { get; } =
        TypeUnionHelper.CalculateHash(Types);

    public override ImmutableArray<Type> ProxyTypes => Types;

    public override int GetHashCode() => Hash;
}

public class TypeUnion<T1, T2, T3, T4, T5> : TypeUnionBase
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

    public override ImmutableArray<Type> ProxyTypes => Types;

    public override int GetHashCode() => Hash;
}

public class TypeUnion<T1, T2, T3, T4, T5, T6> : TypeUnionBase
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

    public override ImmutableArray<Type> ProxyTypes => Types;

    public override int GetHashCode() => Hash;
}

public class TypeUnion<T1, T2, T3, T4, T5, T6, T7> : TypeUnionBase
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

    public override ImmutableArray<Type> ProxyTypes => Types;

    public override int GetHashCode() => Hash;
}

public class TypeUnion<T1, T2, T3, T4, T5, T6, T7, T8> : TypeUnionBase
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

    public override ImmutableArray<Type> ProxyTypes => Types;

    public override int GetHashCode() => Hash;
}

public class TypeUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9> : TypeUnionBase
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

    public override ImmutableArray<Type> ProxyTypes => Types;

    public override int GetHashCode() => Hash;
}

public class TypeUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> : TypeUnionBase
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

    public override ImmutableArray<Type> ProxyTypes => Types;

    public override int GetHashCode() => Hash;
}

public class TypeUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11> : TypeUnionBase
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

    public override ImmutableArray<Type> ProxyTypes => Types;

    public override int GetHashCode() => Hash;
}

public class TypeUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12> : TypeUnionBase
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

    public override ImmutableArray<Type> ProxyTypes => Types;

    public override int GetHashCode() => Hash;
}

public class TypeUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13> : TypeUnionBase
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

    public override ImmutableArray<Type> ProxyTypes => Types;

    public override int GetHashCode() => Hash;
}

public class TypeUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14> : TypeUnionBase
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

    public override ImmutableArray<Type> ProxyTypes => Types;

    public override int GetHashCode() => Hash;
}

public class TypeUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15> : TypeUnionBase
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

    public override ImmutableArray<Type> ProxyTypes => Types;

    public override int GetHashCode() => Hash;
}

public class TypeUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16> : TypeUnionBase
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

    public override ImmutableArray<Type> ProxyTypes => Types;

    public override int GetHashCode() => Hash;
}