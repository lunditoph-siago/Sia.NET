namespace Sia;

using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;

public static class TypeUnionHelper
{
    public static IComparer<Type> TypeComparer { get; }
        = Comparer<Type>.Create((a, b) =>
            a.GetHashCode().CompareTo(b.GetHashCode()));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ImmutableArray<Type> CreateSortedArray(Span<Type> types)
    {
        var set = new SortedSet<Type>(TypeComparer);
        foreach (var type in types) {
            set.Add(type);
        }
        return [..set];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ImmutableArray<Type> CreateSortedArray(IEnumerable<Type> types)
        => [..new SortedSet<Type>(types, TypeComparer)];

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
    public ImmutableArray<Type> Types { get; }
    public FrozenSet<Type> TypeSet { get; }

    private readonly int _hashCode;

    public TypeUnion(IEnumerable<Type> types)
    {
        Types = TypeUnionHelper.CreateSortedArray(types);
        TypeSet = types.ToFrozenSet();
        _hashCode = TypeUnionHelper.CalculateHash(Types);
    }

    public TypeUnion(params Type[] types)
    {
        Types = TypeUnionHelper.CreateSortedArray(types.AsSpan());
        TypeSet = types.ToFrozenSet();
        _hashCode = TypeUnionHelper.CalculateHash(Types);
    }

    public override int GetHashCode() => _hashCode;
    public bool Equals(ITypeUnion? other) => TypeUnionHelper.Equals(this, other);

    public ITypeUnion Merge(ITypeUnion other)
        => new TypeUnion(Types.AddRange(other.Types));
}

public abstract class StaticTypeUnionBase : IStaticTypeUnion
{
    public abstract FrozenSet<Type> TypeSet { get; }
    public abstract ImmutableArray<Type> Types { get; }

    public bool Equals(ITypeUnion? other) => TypeUnionHelper.Equals(this, other);

    public ITypeUnion Merge(ITypeUnion other)
        => new TypeUnion(Types.AddRange(other.Types));
}

public class TypeUnion<T1> : StaticTypeUnionBase
{
    public static ImmutableArray<Type> StaticTypes { get; }
        = TypeUnionHelper.CreateSortedArray([typeof(T1)]);

    public static FrozenSet<Type> StaticTypeSet { get; } = StaticTypes.ToFrozenSet();
    public static int StaticHash { get; } = typeof(T1).GetHashCode();

    public override ImmutableArray<Type> Types => StaticTypes;
    public override FrozenSet<Type> TypeSet => StaticTypeSet;

    public override int GetHashCode() => StaticHash;
}

public class TypeUnion<T1, T2> : StaticTypeUnionBase
{
    public static ImmutableArray<Type> StaticTypes { get; }
        = TypeUnionHelper.CreateSortedArray([typeof(T1), typeof(T2)]);

    public static FrozenSet<Type> StaticTypeSet { get; } = StaticTypes.ToFrozenSet();
    public static int StaticHash { get; } = TypeUnionHelper.CalculateHash(StaticTypes);

    public override ImmutableArray<Type> Types => StaticTypes;
    public override FrozenSet<Type> TypeSet => StaticTypeSet;

    public override int GetHashCode() => StaticHash;
}

public class TypeUnion<T1, T2, T3> : StaticTypeUnionBase
{
    public static ImmutableArray<Type> StaticTypes { get; }
        = TypeUnionHelper.CreateSortedArray([typeof(T1), typeof(T2), typeof(T3)]);

    public static FrozenSet<Type> StaticTypeSet { get; } = StaticTypes.ToFrozenSet();
    public static int StaticHash { get; } = TypeUnionHelper.CalculateHash(StaticTypes);

    public override ImmutableArray<Type> Types => StaticTypes;
    public override FrozenSet<Type> TypeSet => StaticTypeSet;

    public override int GetHashCode() => StaticHash;
}

public class TypeUnion<T1, T2, T3, T4> : StaticTypeUnionBase
{
    public static ImmutableArray<Type> StaticTypes { get; }
        = TypeUnionHelper.CreateSortedArray([typeof(T1), typeof(T2), typeof(T3), typeof(T4)]);

    public static FrozenSet<Type> StaticTypeSet { get; } = StaticTypes.ToFrozenSet();
    public static int StaticHash { get; } = TypeUnionHelper.CalculateHash(StaticTypes);

    public override ImmutableArray<Type> Types => StaticTypes;
    public override FrozenSet<Type> TypeSet => StaticTypeSet;

    public override int GetHashCode() => StaticHash;
}

public class TypeUnion<T1, T2, T3, T4, T5> : StaticTypeUnionBase
{
    public static ImmutableArray<Type> StaticTypes { get; }
        = TypeUnionHelper.CreateSortedArray([typeof(T1), typeof(T2), typeof(T3), typeof(T4), typeof(T5)]);

    public static FrozenSet<Type> StaticTypeSet { get; } = StaticTypes.ToFrozenSet();
    public static int StaticHash { get; } = TypeUnionHelper.CalculateHash(StaticTypes);

    public override ImmutableArray<Type> Types => StaticTypes;
    public override FrozenSet<Type> TypeSet => StaticTypeSet;

    public override int GetHashCode() => StaticHash;
}

public class TypeUnion<T1, T2, T3, T4, T5, T6> : StaticTypeUnionBase
{
    public static ImmutableArray<Type> StaticTypes { get; }
        = TypeUnionHelper.CreateSortedArray([typeof(T1), typeof(T2), typeof(T3), typeof(T4), typeof(T5), typeof(T6)]);

    public static FrozenSet<Type> StaticTypeSet { get; } = StaticTypes.ToFrozenSet();
    public static int StaticHash { get; } = TypeUnionHelper.CalculateHash(StaticTypes);

    public override ImmutableArray<Type> Types => StaticTypes;
    public override FrozenSet<Type> TypeSet => StaticTypeSet;

    public override int GetHashCode() => StaticHash;
}

public class TypeUnion<T1, T2, T3, T4, T5, T6, T7> : StaticTypeUnionBase
{
    public static ImmutableArray<Type> StaticTypes { get; }
        = [typeof(T1), typeof(T2), typeof(T3), typeof(T4), typeof(T5), typeof(T6), typeof(T7)];

    public static FrozenSet<Type> StaticTypeSet { get; } = StaticTypes.ToFrozenSet();
    public static int StaticHash { get; } = TypeUnionHelper.CalculateHash(StaticTypes);

    public override ImmutableArray<Type> Types => StaticTypes;
    public override FrozenSet<Type> TypeSet => StaticTypeSet;

    public override int GetHashCode() => StaticHash;
}

public class TypeUnion<T1, T2, T3, T4, T5, T6, T7, T8> : StaticTypeUnionBase
{
    public static ImmutableArray<Type> StaticTypes { get; }
        = TypeUnionHelper.CreateSortedArray([typeof(T1), typeof(T2), typeof(T3), typeof(T4), typeof(T5), typeof(T6), typeof(T7), typeof(T8)]);

    public static FrozenSet<Type> StaticTypeSet { get; } = StaticTypes.ToFrozenSet();
    public static int StaticHash { get; } = TypeUnionHelper.CalculateHash(StaticTypes);

    public override ImmutableArray<Type> Types => StaticTypes;
    public override FrozenSet<Type> TypeSet => StaticTypeSet;

    public override int GetHashCode() => StaticHash;
}

public class TypeUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9> : StaticTypeUnionBase
{
    public static ImmutableArray<Type> StaticTypes { get; }
        = TypeUnionHelper.CreateSortedArray([typeof(T1), typeof(T2), typeof(T3), typeof(T4), typeof(T5), typeof(T6), typeof(T7), typeof(T8), typeof(T9)]);

    public static FrozenSet<Type> StaticTypeSet { get; } = StaticTypes.ToFrozenSet();
    public static int StaticHash { get; } = TypeUnionHelper.CalculateHash(StaticTypes);

    public override ImmutableArray<Type> Types => StaticTypes;
    public override FrozenSet<Type> TypeSet => StaticTypeSet;

    public override int GetHashCode() => StaticHash;
}

public class TypeUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> : StaticTypeUnionBase
{
    public static ImmutableArray<Type> StaticTypes { get; }
        = TypeUnionHelper.CreateSortedArray([typeof(T1), typeof(T2), typeof(T3), typeof(T4), typeof(T5), typeof(T6), typeof(T7), typeof(T8), typeof(T9), typeof(T10)]);

    public static FrozenSet<Type> StaticTypeSet { get; } = StaticTypes.ToFrozenSet();
    public static int StaticHash { get; } = TypeUnionHelper.CalculateHash(StaticTypes);

    public override ImmutableArray<Type> Types => StaticTypes;
    public override FrozenSet<Type> TypeSet => StaticTypeSet;

    public override int GetHashCode() => StaticHash;
}

public class TypeUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11> : StaticTypeUnionBase
{
    public static ImmutableArray<Type> StaticTypes { get; }
        = [typeof(T1), typeof(T2), typeof(T3), typeof(T4), typeof(T5), typeof(T6), typeof(T7), typeof(T8), typeof(T9), typeof(T10), typeof(T11)];

    public static FrozenSet<Type> StaticTypeSet { get; } = StaticTypes.ToFrozenSet();
    public static int StaticHash { get; } = TypeUnionHelper.CalculateHash(StaticTypes);

    public override ImmutableArray<Type> Types => StaticTypes;
    public override FrozenSet<Type> TypeSet => StaticTypeSet;

    public override int GetHashCode() => StaticHash;
}

public class TypeUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12> : StaticTypeUnionBase
{
    public static ImmutableArray<Type> StaticTypes { get; }
        = TypeUnionHelper.CreateSortedArray([typeof(T1), typeof(T2), typeof(T3), typeof(T4), typeof(T5), typeof(T6), typeof(T7), typeof(T8), typeof(T9), typeof(T10), typeof(T11), typeof(T12)]);

    public static FrozenSet<Type> StaticTypeSet { get; } = StaticTypes.ToFrozenSet();
    public static int StaticHash { get; } = TypeUnionHelper.CalculateHash(StaticTypes);

    public override ImmutableArray<Type> Types => StaticTypes;
    public override FrozenSet<Type> TypeSet => StaticTypeSet;

    public override int GetHashCode() => StaticHash;
}

public class TypeUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13> : StaticTypeUnionBase
{
    public static ImmutableArray<Type> StaticTypes { get; }
        = TypeUnionHelper.CreateSortedArray([typeof(T1), typeof(T2), typeof(T3), typeof(T4), typeof(T5), typeof(T6), typeof(T7), typeof(T8), typeof(T9), typeof(T10), typeof(T11), typeof(T12), typeof(T13)]);

    public static FrozenSet<Type> StaticTypeSet { get; } = StaticTypes.ToFrozenSet();
    public static int StaticHash { get; } = TypeUnionHelper.CalculateHash(StaticTypes);

    public override ImmutableArray<Type> Types => StaticTypes;
    public override FrozenSet<Type> TypeSet => StaticTypeSet;

    public override int GetHashCode() => StaticHash;
}

public class TypeUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14> : StaticTypeUnionBase
{
    public static ImmutableArray<Type> StaticTypes { get; }
        = [typeof(T1), typeof(T2), typeof(T3), typeof(T4), typeof(T5), typeof(T6), typeof(T7), typeof(T8), typeof(T9), typeof(T10), typeof(T11), typeof(T12), typeof(T13), typeof(T14)];

    public static FrozenSet<Type> StaticTypeSet { get; } = StaticTypes.ToFrozenSet();
    public static int StaticHash { get; } = TypeUnionHelper.CalculateHash(StaticTypes);

    public override ImmutableArray<Type> Types => StaticTypes;
    public override FrozenSet<Type> TypeSet => StaticTypeSet;

    public override int GetHashCode() => StaticHash;
}

public class TypeUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15> : StaticTypeUnionBase
{
    public static ImmutableArray<Type> StaticTypes { get; }
        = TypeUnionHelper.CreateSortedArray([typeof(T1), typeof(T2), typeof(T3), typeof(T4), typeof(T5), typeof(T6), typeof(T7), typeof(T8), typeof(T9), typeof(T10), typeof(T11), typeof(T12), typeof(T13), typeof(T14), typeof(T15)]);

    public static FrozenSet<Type> StaticTypeSet { get; } = StaticTypes.ToFrozenSet();
    public static int StaticHash { get; } = TypeUnionHelper.CalculateHash(StaticTypes);

    public override ImmutableArray<Type> Types => StaticTypes;
    public override FrozenSet<Type> TypeSet => StaticTypeSet;

    public override int GetHashCode() => StaticHash;
}

public class TypeUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16> : StaticTypeUnionBase
{
    public static ImmutableArray<Type> StaticTypes { get; }
        = TypeUnionHelper.CreateSortedArray([typeof(T1), typeof(T2), typeof(T3), typeof(T4), typeof(T5), typeof(T6), typeof(T7), typeof(T8), typeof(T9), typeof(T10), typeof(T11), typeof(T12), typeof(T13), typeof(T14), typeof(T15), typeof(T16)]);

    public static FrozenSet<Type> StaticTypeSet { get; } = StaticTypes.ToFrozenSet();
    public static int StaticHash { get; } = TypeUnionHelper.CalculateHash(StaticTypes);

    public override ImmutableArray<Type> Types => StaticTypes;
    public override FrozenSet<Type> TypeSet => StaticTypeSet;

    public override int GetHashCode() => StaticHash;
}