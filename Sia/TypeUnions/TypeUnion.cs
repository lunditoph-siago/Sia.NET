namespace Sia;

using System.Collections.Immutable;
using System.Runtime.CompilerServices;

internal static class TypeUnionHelper
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ImmutableSortedDictionary<int, Type> CreateDict(params (int, Type)[] types)
    {
        var builder = ImmutableSortedDictionary.CreateBuilder<int, Type>();
        foreach (var t in types) {
            builder.Add(t.Item1, t.Item2);
        }
        return builder.ToImmutableSortedDictionary();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Equals(ITypeUnion lhs, ITypeUnion? rhs)
    {
        if (lhs == rhs) { return true; }
        if (rhs == null) { return false; }
        if (lhs.GetHashCode() != rhs.GetHashCode()) { return false; }

        var typesA = lhs.Types;
        var typesB = rhs.Types;

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

public class TypeUnion : ITypeUnion
{
    public ImmutableSortedDictionary<int, Type> IndexTypeDictionary { get; }
    public ImmutableArray<Type> Types { get; }

    private readonly int _hashCode;

    public TypeUnion(ImmutableSortedDictionary<int, Type> indexTypeDictionary)
    {
        IndexTypeDictionary = indexTypeDictionary;
        Types = indexTypeDictionary.Values.ToImmutableArray();
        _hashCode = TypeUnionHelper.CalculateHash(Types);
    }

    public override int GetHashCode() => _hashCode;
    public bool Equals(ITypeUnion? other) => TypeUnionHelper.Equals(this, other);

    public ITypeUnion Merge(ITypeUnion other)
        => new TypeUnion(IndexTypeDictionary.AddRange(other.IndexTypeDictionary));
}

public abstract class StaticTypeUnionBase : IStaticTypeUnion
{
    public abstract ImmutableArray<Type> Types { get; }
    public abstract ImmutableSortedDictionary<int, Type> IndexTypeDictionary { get; }

    public bool Equals(ITypeUnion? other) => TypeUnionHelper.Equals(this, other);

    public ITypeUnion Merge(ITypeUnion other)
        => new TypeUnion(IndexTypeDictionary.AddRange(other.IndexTypeDictionary));
}

public class TypeUnion<T1> : StaticTypeUnionBase
{
    public static ImmutableSortedDictionary<int, Type> StaticIndexTypeDictionary { get; }
        = TypeUnionHelper.CreateDict((TypeIndexer<T1>.Index, typeof(T1)));

    public static ImmutableArray<Type> StaticTypes { get; } = StaticIndexTypeDictionary.Values.ToImmutableArray();
    public static int StaticHash { get; } = typeof(T1).GetHashCode();

    public override ImmutableArray<Type> Types => StaticTypes;
    public override ImmutableSortedDictionary<int, Type> IndexTypeDictionary => StaticIndexTypeDictionary;

    public override int GetHashCode() => StaticHash;
}

public class TypeUnion<T1, T2> : StaticTypeUnionBase
{
    public static ImmutableSortedDictionary<int, Type> StaticIndexTypeDictionary { get; }
        = TypeUnionHelper.CreateDict(
            (TypeIndexer<T1>.Index, typeof(T1)),
            (TypeIndexer<T2>.Index, typeof(T2)));

    public static ImmutableArray<Type> StaticTypes { get; } = StaticIndexTypeDictionary.Values.ToImmutableArray();
    public static int StaticHash { get; } = TypeUnionHelper.CalculateHash(StaticTypes);

    public override ImmutableArray<Type> Types => StaticTypes;
    public override ImmutableSortedDictionary<int, Type> IndexTypeDictionary => StaticIndexTypeDictionary;

    public override int GetHashCode() => StaticHash;
}

public class TypeUnion<T1, T2, T3> : StaticTypeUnionBase
{
    public static ImmutableSortedDictionary<int, Type> StaticIndexTypeDictionary { get; }
        = TypeUnionHelper.CreateDict(
            (TypeIndexer<T1>.Index, typeof(T1)),
            (TypeIndexer<T2>.Index, typeof(T2)),
            (TypeIndexer<T3>.Index, typeof(T3)));

    public static ImmutableArray<Type> StaticTypes { get; } = StaticIndexTypeDictionary.Values.ToImmutableArray();
    public static int StaticHash { get; } = TypeUnionHelper.CalculateHash(StaticTypes);

    public override ImmutableArray<Type> Types => StaticTypes;
    public override ImmutableSortedDictionary<int, Type> IndexTypeDictionary => StaticIndexTypeDictionary;

    public override int GetHashCode() => StaticHash;
}

public class TypeUnion<T1, T2, T3, T4> : StaticTypeUnionBase
{
    public static ImmutableSortedDictionary<int, Type> StaticIndexTypeDictionary { get; }
        = TypeUnionHelper.CreateDict(
            (TypeIndexer<T1>.Index, typeof(T1)),
            (TypeIndexer<T2>.Index, typeof(T2)),
            (TypeIndexer<T3>.Index, typeof(T3)),
            (TypeIndexer<T4>.Index, typeof(T4)));

    public static ImmutableArray<Type> StaticTypes { get; } = StaticIndexTypeDictionary.Values.ToImmutableArray();
    public static int StaticHash { get; } = TypeUnionHelper.CalculateHash(StaticTypes);

    public override ImmutableArray<Type> Types => StaticTypes;
    public override ImmutableSortedDictionary<int, Type> IndexTypeDictionary => StaticIndexTypeDictionary;

    public override int GetHashCode() => StaticHash;
}

public class TypeUnion<T1, T2, T3, T4, T5> : StaticTypeUnionBase
{
    public static ImmutableSortedDictionary<int, Type> StaticIndexTypeDictionary { get; }
        = TypeUnionHelper.CreateDict(
            (TypeIndexer<T1>.Index, typeof(T1)),
            (TypeIndexer<T2>.Index, typeof(T2)),
            (TypeIndexer<T3>.Index, typeof(T3)),
            (TypeIndexer<T4>.Index, typeof(T4)),
            (TypeIndexer<T5>.Index, typeof(T5)));

    public static ImmutableArray<Type> StaticTypes { get; } = StaticIndexTypeDictionary.Values.ToImmutableArray();
    public static int StaticHash { get; } = TypeUnionHelper.CalculateHash(StaticTypes);

    public override ImmutableArray<Type> Types => StaticTypes;
    public override ImmutableSortedDictionary<int, Type> IndexTypeDictionary => StaticIndexTypeDictionary;

    public override int GetHashCode() => StaticHash;
}

public class TypeUnion<T1, T2, T3, T4, T5, T6> : StaticTypeUnionBase
{
    public static ImmutableSortedDictionary<int, Type> StaticIndexTypeDictionary { get; }
        = TypeUnionHelper.CreateDict(
            (TypeIndexer<T1>.Index, typeof(T1)),
            (TypeIndexer<T2>.Index, typeof(T2)),
            (TypeIndexer<T3>.Index, typeof(T3)),
            (TypeIndexer<T4>.Index, typeof(T4)),
            (TypeIndexer<T5>.Index, typeof(T5)),
            (TypeIndexer<T6>.Index, typeof(T6)));

    public static ImmutableArray<Type> StaticTypes { get; } = StaticIndexTypeDictionary.Values.ToImmutableArray();
    public static int StaticHash { get; } = TypeUnionHelper.CalculateHash(StaticTypes);

    public override ImmutableArray<Type> Types => StaticTypes;
    public override ImmutableSortedDictionary<int, Type> IndexTypeDictionary => StaticIndexTypeDictionary;

    public override int GetHashCode() => StaticHash;
}

public class TypeUnion<T1, T2, T3, T4, T5, T6, T7> : StaticTypeUnionBase
{
    public static ImmutableSortedDictionary<int, Type> StaticIndexTypeDictionary { get; }
        = TypeUnionHelper.CreateDict(
            (TypeIndexer<T1>.Index, typeof(T1)),
            (TypeIndexer<T2>.Index, typeof(T2)),
            (TypeIndexer<T3>.Index, typeof(T3)),
            (TypeIndexer<T4>.Index, typeof(T4)),
            (TypeIndexer<T5>.Index, typeof(T5)),
            (TypeIndexer<T6>.Index, typeof(T6)),
            (TypeIndexer<T7>.Index, typeof(T7)));

    public static ImmutableArray<Type> StaticTypes { get; } = StaticIndexTypeDictionary.Values.ToImmutableArray();
    public static int StaticHash { get; } = TypeUnionHelper.CalculateHash(StaticTypes);

    public override ImmutableArray<Type> Types => StaticTypes;
    public override ImmutableSortedDictionary<int, Type> IndexTypeDictionary => StaticIndexTypeDictionary;

    public override int GetHashCode() => StaticHash;
}

public class TypeUnion<T1, T2, T3, T4, T5, T6, T7, T8> : StaticTypeUnionBase
{
    public static ImmutableSortedDictionary<int, Type> StaticIndexTypeDictionary { get; }
        = TypeUnionHelper.CreateDict(
            (TypeIndexer<T1>.Index, typeof(T1)),
            (TypeIndexer<T2>.Index, typeof(T2)),
            (TypeIndexer<T3>.Index, typeof(T3)),
            (TypeIndexer<T4>.Index, typeof(T4)),
            (TypeIndexer<T5>.Index, typeof(T5)),
            (TypeIndexer<T6>.Index, typeof(T6)),
            (TypeIndexer<T7>.Index, typeof(T7)),
            (TypeIndexer<T8>.Index, typeof(T8)));

    public static ImmutableArray<Type> StaticTypes { get; } = StaticIndexTypeDictionary.Values.ToImmutableArray();
    public static int StaticHash { get; } = TypeUnionHelper.CalculateHash(StaticTypes);

    public override ImmutableArray<Type> Types => StaticTypes;
    public override ImmutableSortedDictionary<int, Type> IndexTypeDictionary => StaticIndexTypeDictionary;

    public override int GetHashCode() => StaticHash;
}

public class TypeUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9> : StaticTypeUnionBase
{
    public static ImmutableSortedDictionary<int, Type> StaticIndexTypeDictionary { get; }
        = TypeUnionHelper.CreateDict(
            (TypeIndexer<T1>.Index, typeof(T1)),
            (TypeIndexer<T2>.Index, typeof(T2)),
            (TypeIndexer<T3>.Index, typeof(T3)),
            (TypeIndexer<T4>.Index, typeof(T4)),
            (TypeIndexer<T5>.Index, typeof(T5)),
            (TypeIndexer<T6>.Index, typeof(T6)),
            (TypeIndexer<T7>.Index, typeof(T7)),
            (TypeIndexer<T8>.Index, typeof(T8)),
            (TypeIndexer<T9>.Index, typeof(T9)));

    public static ImmutableArray<Type> StaticTypes { get; } = StaticIndexTypeDictionary.Values.ToImmutableArray();
    public static int StaticHash { get; } = TypeUnionHelper.CalculateHash(StaticTypes);

    public override ImmutableArray<Type> Types => StaticTypes;
    public override ImmutableSortedDictionary<int, Type> IndexTypeDictionary => StaticIndexTypeDictionary;

    public override int GetHashCode() => StaticHash;
}

public class TypeUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> : StaticTypeUnionBase
{
    public static ImmutableSortedDictionary<int, Type> StaticIndexTypeDictionary { get; }
        = TypeUnionHelper.CreateDict(
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

    public static ImmutableArray<Type> StaticTypes { get; } = StaticIndexTypeDictionary.Values.ToImmutableArray();
    public static int StaticHash { get; } = TypeUnionHelper.CalculateHash(StaticTypes);

    public override ImmutableArray<Type> Types => StaticTypes;
    public override ImmutableSortedDictionary<int, Type> IndexTypeDictionary => StaticIndexTypeDictionary;

    public override int GetHashCode() => StaticHash;
}

public class TypeUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11> : StaticTypeUnionBase
{
    public static ImmutableSortedDictionary<int, Type> StaticIndexTypeDictionary { get; }
        = TypeUnionHelper.CreateDict(
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

    public static ImmutableArray<Type> StaticTypes { get; } = StaticIndexTypeDictionary.Values.ToImmutableArray();
    public static int StaticHash { get; } = TypeUnionHelper.CalculateHash(StaticTypes);

    public override ImmutableArray<Type> Types => StaticTypes;
    public override ImmutableSortedDictionary<int, Type> IndexTypeDictionary => StaticIndexTypeDictionary;

    public override int GetHashCode() => StaticHash;
}

public class TypeUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12> : StaticTypeUnionBase
{
    public static ImmutableSortedDictionary<int, Type> StaticIndexTypeDictionary { get; }
        = TypeUnionHelper.CreateDict(
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

    public static ImmutableArray<Type> StaticTypes { get; } = StaticIndexTypeDictionary.Values.ToImmutableArray();
    public static int StaticHash { get; } = TypeUnionHelper.CalculateHash(StaticTypes);

    public override ImmutableArray<Type> Types => StaticTypes;
    public override ImmutableSortedDictionary<int, Type> IndexTypeDictionary => StaticIndexTypeDictionary;

    public override int GetHashCode() => StaticHash;
}

public class TypeUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13> : StaticTypeUnionBase
{
    public static ImmutableSortedDictionary<int, Type> StaticIndexTypeDictionary { get; }
        = TypeUnionHelper.CreateDict(
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

    public static ImmutableArray<Type> StaticTypes { get; } = StaticIndexTypeDictionary.Values.ToImmutableArray();
    public static int StaticHash { get; } = TypeUnionHelper.CalculateHash(StaticTypes);

    public override ImmutableArray<Type> Types => StaticTypes;
    public override ImmutableSortedDictionary<int, Type> IndexTypeDictionary => StaticIndexTypeDictionary;

    public override int GetHashCode() => StaticHash;
}

public class TypeUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14> : StaticTypeUnionBase
{
    public static ImmutableSortedDictionary<int, Type> StaticIndexTypeDictionary { get; }
        = TypeUnionHelper.CreateDict(
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

    public static ImmutableArray<Type> StaticTypes { get; } = StaticIndexTypeDictionary.Values.ToImmutableArray();
    public static int StaticHash { get; } = TypeUnionHelper.CalculateHash(StaticTypes);

    public override ImmutableArray<Type> Types => StaticTypes;
    public override ImmutableSortedDictionary<int, Type> IndexTypeDictionary => StaticIndexTypeDictionary;

    public override int GetHashCode() => StaticHash;
}

public class TypeUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15> : StaticTypeUnionBase
{
    public static ImmutableSortedDictionary<int, Type> StaticIndexTypeDictionary { get; }
        = TypeUnionHelper.CreateDict(
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

    public static ImmutableArray<Type> StaticTypes { get; } = StaticIndexTypeDictionary.Values.ToImmutableArray();
    public static int StaticHash { get; } = TypeUnionHelper.CalculateHash(StaticTypes);

    public override ImmutableArray<Type> Types => StaticTypes;
    public override ImmutableSortedDictionary<int, Type> IndexTypeDictionary => StaticIndexTypeDictionary;

    public override int GetHashCode() => StaticHash;
}

public class TypeUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16> : StaticTypeUnionBase
{
    public static ImmutableSortedDictionary<int, Type> StaticIndexTypeDictionary { get; }
        = TypeUnionHelper.CreateDict(
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

    public static ImmutableArray<Type> StaticTypes { get; } = StaticIndexTypeDictionary.Values.ToImmutableArray();
    public static int StaticHash { get; } = TypeUnionHelper.CalculateHash(StaticTypes);

    public override ImmutableArray<Type> Types => StaticTypes;
    public override ImmutableSortedDictionary<int, Type> IndexTypeDictionary => StaticIndexTypeDictionary;

    public override int GetHashCode() => StaticHash;
}