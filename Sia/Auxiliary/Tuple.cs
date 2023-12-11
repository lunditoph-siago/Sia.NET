namespace Sia;

public struct Tuple<T1> : IComponentBundle
{
    public T1 Item1;
}

public struct Tuple<T1, T2> : IComponentBundle
{
    public T1 Item1;
    public T2 Item2;
}

public struct Tuple<T1, T2, T3> : IComponentBundle
{
    public T1 Item1;
    public T2 Item2;
    public T3 Item3;
}

public struct Tuple<T1, T2, T3, T4> : IComponentBundle
{
    public T1 Item1;
    public T2 Item2;
    public T3 Item3;
    public T4 Item4;
}

public struct Tuple<T1, T2, T3, T4, T5> : IComponentBundle
{
    public T1 Item1;
    public T2 Item2;
    public T3 Item3;
    public T4 Item4;
    public T5 Item5;
}

public struct Tuple<T1, T2, T3, T4, T5, T6> : IComponentBundle
{
    public T1 Item1;
    public T2 Item2;
    public T3 Item3;
    public T4 Item4;
    public T5 Item5;
    public T6 Item6;
}

public struct Tuple<T1, T2, T3, T4, T5, T6, T7> : IComponentBundle
{
    public T1 Item1;
    public T2 Item2;
    public T3 Item3;
    public T4 Item4;
    public T5 Item5;
    public T6 Item6;
    public T7 Item7;
}

public struct Tuple<T1, T2, T3, T4, T5, T6, T7, T8> : IComponentBundle
{
    public T1 Item1;
    public T2 Item2;
    public T3 Item3;
    public T4 Item4;
    public T5 Item5;
    public T6 Item6;
    public T7 Item7;
    public T8 Item8;
}

public struct Tuple<T1, T2, T3, T4, T5, T6, T7, T8, T9> : IComponentBundle
{
    public T1 Item1;
    public T2 Item2;
    public T3 Item3;
    public T4 Item4;
    public T5 Item5;
    public T6 Item6;
    public T7 Item7;
    public T8 Item8;
    public T9 Item9;
}

public struct Tuple<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> : IComponentBundle
{
    public T1 Item1;
    public T2 Item2;
    public T3 Item3;
    public T4 Item4;
    public T5 Item5;
    public T6 Item6;
    public T7 Item7;
    public T8 Item8;
    public T9 Item9;
    public T10 Item10;
}

public struct Tuple<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11> : IComponentBundle
{
    public T1 Item1;
    public T2 Item2;
    public T3 Item3;
    public T4 Item4;
    public T5 Item5;
    public T6 Item6;
    public T7 Item7;
    public T8 Item8;
    public T9 Item9;
    public T10 Item10;
    public T11 Item11;
}

public struct Tuple<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12> : IComponentBundle
{
    public T1 Item1;
    public T2 Item2;
    public T3 Item3;
    public T4 Item4;
    public T5 Item5;
    public T6 Item6;
    public T7 Item7;
    public T8 Item8;
    public T9 Item9;
    public T10 Item10;
    public T11 Item11;
    public T12 Item12;
}

public struct Tuple<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13> : IComponentBundle
{
    public T1 Item1;
    public T2 Item2;
    public T3 Item3;
    public T4 Item4;
    public T5 Item5;
    public T6 Item6;
    public T7 Item7;
    public T8 Item8;
    public T9 Item9;
    public T10 Item10;
    public T11 Item11;
    public T12 Item12;
    public T13 Item13;
}

public struct Tuple<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14> : IComponentBundle
{
    public T1 Item1;
    public T2 Item2;
    public T3 Item3;
    public T4 Item4;
    public T5 Item5;
    public T6 Item6;
    public T7 Item7;
    public T8 Item8;
    public T9 Item9;
    public T10 Item10;
    public T11 Item11;
    public T12 Item12;
    public T13 Item13;
    public T14 Item14;
}

public struct Tuple<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15> : IComponentBundle
{
    public T1 Item1;
    public T2 Item2;
    public T3 Item3;
    public T4 Item4;
    public T5 Item5;
    public T6 Item6;
    public T7 Item7;
    public T8 Item8;
    public T9 Item9;
    public T10 Item10;
    public T11 Item11;
    public T12 Item12;
    public T13 Item13;
    public T14 Item14;
    public T15 Item15;
}

public struct Tuple<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16> : IComponentBundle
{
    public T1 Item1;
    public T2 Item2;
    public T3 Item3;
    public T4 Item4;
    public T5 Item5;
    public T6 Item6;
    public T7 Item7;
    public T8 Item8;
    public T9 Item9;
    public T10 Item10;
    public T11 Item11;
    public T12 Item12;
    public T13 Item13;
    public T14 Item14;
    public T15 Item15;
    public T16 Item16;
}

public static class Tuple
{
    public static Tuple<T1> Create<T1>(in T1 item1)
        => new() {
            Item1 = item1
        };

    public static Tuple<T1, T2> Create<T1, T2>(in T1 item1, in T2 item2)
        => new() {
            Item1 = item1,
            Item2 = item2
        };

    public static Tuple<T1, T2, T3> Create<T1, T2, T3>(in T1 item1, in T2 item2, in T3 item3)
        => new() {
            Item1 = item1,
            Item2 = item2,
            Item3 = item3
        };

    public static Tuple<T1, T2, T3, T4> Create<T1, T2, T3, T4>(in T1 item1, in T2 item2, in T3 item3, in T4 item4)
        => new() {
            Item1 = item1,
            Item2 = item2,
            Item3 = item3,
            Item4 = item4
        };

    public static Tuple<T1, T2, T3, T4, T5> Create<T1, T2, T3, T4, T5>(in T1 item1, in T2 item2, in T3 item3, in T4 item4, in T5 item5)
        => new() {
            Item1 = item1,
            Item2 = item2,
            Item3 = item3,
            Item4 = item4,
            Item5 = item5
        };

    public static Tuple<T1, T2, T3, T4, T5, T6> Create<T1, T2, T3, T4, T5, T6>(in T1 item1, in T2 item2, in T3 item3, in T4 item4, in T5 item5, in T6 item6)
        => new() {
            Item1 = item1,
            Item2 = item2,
            Item3 = item3,
            Item4 = item4,
            Item5 = item5,
            Item6 = item6
        };

    public static Tuple<T1, T2, T3, T4, T5, T6, T7> Create<T1, T2, T3, T4, T5, T6, T7>(in T1 item1, in T2 item2, in T3 item3, in T4 item4, in T5 item5, in T6 item6, in T7 item7)
        => new() {
            Item1 = item1,
            Item2 = item2,
            Item3 = item3,
            Item4 = item4,
            Item5 = item5,
            Item6 = item6,
            Item7 = item7
        };

    public static Tuple<T1, T2, T3, T4, T5, T6, T7, T8> Create<T1, T2, T3, T4, T5, T6, T7, T8>(in T1 item1, in T2 item2, in T3 item3, in T4 item4, in T5 item5, in T6 item6, in T7 item7, in T8 item8)
        => new() {
            Item1 = item1,
            Item2 = item2,
            Item3 = item3,
            Item4 = item4,
            Item5 = item5,
            Item6 = item6,
            Item7 = item7,
            Item8 = item8
        };

    public static Tuple<T1, T2, T3, T4, T5, T6, T7, T8, T9> Create<T1, T2, T3, T4, T5, T6, T7, T8, T9>(in T1 item1, in T2 item2, in T3 item3, in T4 item4, in T5 item5, in T6 item6, in T7 item7, in T8 item8, in T9 item9)
        => new() {
            Item1 = item1,
            Item2 = item2,
            Item3 = item3,
            Item4 = item4,
            Item5 = item5,
            Item6 = item6,
            Item7 = item7,
            Item8 = item8,
            Item9 = item9
        };

    public static Tuple<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> Create<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(in T1 item1, in T2 item2, in T3 item3, in T4 item4, in T5 item5, in T6 item6, in T7 item7, in T8 item8, in T9 item9, in T10 item10)
        => new() {
            Item1 = item1,
            Item2 = item2,
            Item3 = item3,
            Item4 = item4,
            Item5 = item5,
            Item6 = item6,
            Item7 = item7,
            Item8 = item8,
            Item9 = item9,
            Item10 = item10
        };

    public static Tuple<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11> Create<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(in T1 item1, in T2 item2, in T3 item3, in T4 item4, in T5 item5, in T6 item6, in T7 item7, in T8 item8, in T9 item9, in T10 item10, in T11 item11)
        => new() {
            Item1 = item1,
            Item2 = item2,
            Item3 = item3,
            Item4 = item4,
            Item5 = item5,
            Item6 = item6,
            Item7 = item7,
            Item8 = item8,
            Item9 = item9,
            Item10 = item10,
            Item11 = item11
        };

    public static Tuple<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12> Create<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(in T1 item1, in T2 item2, in T3 item3, in T4 item4, in T5 item5, in T6 item6, in T7 item7, in T8 item8, in T9 item9, in T10 item10, in T11 item11, in T12 item12)
        => new() {
            Item1 = item1,
            Item2 = item2,
            Item3 = item3,
            Item4 = item4,
            Item5 = item5,
            Item6 = item6,
            Item7 = item7,
            Item8 = item8,
            Item9 = item9,
            Item10 = item10,
            Item11 = item11,
            Item12 = item12
        };

    public static Tuple<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13> Create<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>(in T1 item1, in T2 item2, in T3 item3, in T4 item4, in T5 item5, in T6 item6, in T7 item7, in T8 item8, in T9 item9, in T10 item10, in T11 item11, in T12 item12, in T13 item13)
        => new() {
            Item1 = item1,
            Item2 = item2,
            Item3 = item3,
            Item4 = item4,
            Item5 = item5,
            Item6 = item6,
            Item7 = item7,
            Item8 = item8,
            Item9 = item9,
            Item10 = item10,
            Item11 = item11,
            Item12 = item12,
            Item13 = item13
        };

    public static Tuple<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14> Create<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>(in T1 item1, in T2 item2, in T3 item3, in T4 item4, in T5 item5, in T6 item6, in T7 item7, in T8 item8, in T9 item9, in T10 item10, in T11 item11, in T12 item12, in T13 item13, in T14 item14)
        => new() {
            Item1 = item1,
            Item2 = item2,
            Item3 = item3,
            Item4 = item4,
            Item5 = item5,
            Item6 = item6,
            Item7 = item7,
            Item8 = item8,
            Item9 = item9,
            Item10 = item10,
            Item11 = item11,
            Item12 = item12,
            Item13 = item13,
            Item14 = item14
        };

    public static Tuple<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15> Create<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15>(in T1 item1, in T2 item2, in T3 item3, in T4 item4, in T5 item5, in T6 item6, in T7 item7, in T8 item8, in T9 item9, in T10 item10, in T11 item11, in T12 item12, in T13 item13, in T14 item14, in T15 item15)
        => new() {
            Item1 = item1,
            Item2 = item2,
            Item3 = item3,
            Item4 = item4,
            Item5 = item5,
            Item6 = item6,
            Item7 = item7,
            Item8 = item8,
            Item9 = item9,
            Item10 = item10,
            Item11 = item11,
            Item12 = item12,
            Item13 = item13,
            Item14 = item14,
            Item15 = item15
        };

    public static Tuple<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16> Create<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16>(in T1 item1, in T2 item2, in T3 item3, in T4 item4, in T5 item5, in T6 item6, in T7 item7, in T8 item8, in T9 item9, in T10 item10, in T11 item11, in T12 item12, in T13 item13, in T14 item14, in T15 item15, in T16 item16)
        => new() {
            Item1 = item1,
            Item2 = item2,
            Item3 = item3,
            Item4 = item4,
            Item5 = item5,
            Item6 = item6,
            Item7 = item7,
            Item8 = item8,
            Item9 = item9,
            Item10 = item10,
            Item11 = item11,
            Item12 = item12,
            Item13 = item13,
            Item14 = item14,
            Item15 = item15,
            Item16 = item16
        };
}