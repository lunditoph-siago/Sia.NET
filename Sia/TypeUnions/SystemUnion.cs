namespace Sia;

public class SystemUnion<T1>
    : TypeUnion<T1>, ISystemUnion
    where T1 : ISystem, new()
{
    static SystemUnion()
    {
        SystemLibrary.Ensure<T1>();
    }
}

public class SystemUnion<T1, T2>
    : TypeUnion<T1, T2>, ISystemUnion
    where T1 : ISystem, new()
    where T2 : ISystem, new()
{
    static SystemUnion()
    {
        SystemLibrary.Ensure<T1>();
        SystemLibrary.Ensure<T2>();
    }
}

public class SystemUnion<T1, T2, T3>
    : TypeUnion<T1, T2, T3>, ISystemUnion
    where T1 : ISystem, new()
    where T2 : ISystem, new()
    where T3 : ISystem, new()
{
    static SystemUnion()
    {
        SystemLibrary.Ensure<T1>();
        SystemLibrary.Ensure<T2>();
        SystemLibrary.Ensure<T3>();
    }
}

public class SystemUnion<T1, T2, T3, T4>
    : TypeUnion<T1, T2, T3, T4>, ISystemUnion
    where T1 : ISystem, new()
    where T2 : ISystem, new()
    where T3 : ISystem, new()
    where T4 : ISystem, new()
{
    static SystemUnion()
    {
        SystemLibrary.Ensure<T1>();
        SystemLibrary.Ensure<T2>();
        SystemLibrary.Ensure<T3>();
        SystemLibrary.Ensure<T4>();
    }
}

public class SystemUnion<T1, T2, T3, T4, T5>
    : TypeUnion<T1, T2, T3, T4, T5>, ISystemUnion
    where T1 : ISystem, new()
    where T2 : ISystem, new()
    where T3 : ISystem, new()
    where T4 : ISystem, new()
    where T5 : ISystem, new()
{
    static SystemUnion()
    {
        SystemLibrary.Ensure<T1>();
        SystemLibrary.Ensure<T2>();
        SystemLibrary.Ensure<T3>();
        SystemLibrary.Ensure<T4>();
        SystemLibrary.Ensure<T5>();
    }
}

public class SystemUnion<T1, T2, T3, T4, T5, T6>
    : TypeUnion<T1, T2, T3, T4, T5, T6>, ISystemUnion
    where T1 : ISystem, new()
    where T2 : ISystem, new()
    where T3 : ISystem, new()
    where T4 : ISystem, new()
    where T5 : ISystem, new()
    where T6 : ISystem, new()
{
    static SystemUnion()
    {
        SystemLibrary.Ensure<T1>();
        SystemLibrary.Ensure<T2>();
        SystemLibrary.Ensure<T3>();
        SystemLibrary.Ensure<T4>();
        SystemLibrary.Ensure<T5>();
        SystemLibrary.Ensure<T6>();
    }
}

public class SystemUnion<T1, T2, T3, T4, T5, T6, T7>
    : TypeUnion<T1, T2, T3, T4, T5, T6, T7>, ISystemUnion
    where T1 : ISystem, new()
    where T2 : ISystem, new()
    where T3 : ISystem, new()
    where T4 : ISystem, new()
    where T5 : ISystem, new()
    where T6 : ISystem, new()
    where T7 : ISystem, new()
{
    static SystemUnion()
    {
        SystemLibrary.Ensure<T1>();
        SystemLibrary.Ensure<T2>();
        SystemLibrary.Ensure<T3>();
        SystemLibrary.Ensure<T4>();
        SystemLibrary.Ensure<T5>();
        SystemLibrary.Ensure<T6>();
        SystemLibrary.Ensure<T7>();
    }
}

public class SystemUnion<T1, T2, T3, T4, T5, T6, T7, T8>
    : TypeUnion<T1, T2, T3, T4, T5, T6, T7, T8>, ISystemUnion
    where T1 : ISystem, new()
    where T2 : ISystem, new()
    where T3 : ISystem, new()
    where T4 : ISystem, new()
    where T5 : ISystem, new()
    where T6 : ISystem, new()
    where T7 : ISystem, new()
    where T8 : ISystem, new()
{
    static SystemUnion()
    {
        SystemLibrary.Ensure<T1>();
        SystemLibrary.Ensure<T2>();
        SystemLibrary.Ensure<T3>();
        SystemLibrary.Ensure<T4>();
        SystemLibrary.Ensure<T5>();
        SystemLibrary.Ensure<T6>();
        SystemLibrary.Ensure<T7>();
        SystemLibrary.Ensure<T8>();
    }
}

public class SystemUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9>
    : TypeUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9>, ISystemUnion
    where T1 : ISystem, new()
    where T2 : ISystem, new()
    where T3 : ISystem, new()
    where T4 : ISystem, new()
    where T5 : ISystem, new()
    where T6 : ISystem, new()
    where T7 : ISystem, new()
    where T8 : ISystem, new()
    where T9 : ISystem, new()
{
    static SystemUnion()
    {
        SystemLibrary.Ensure<T1>();
        SystemLibrary.Ensure<T2>();
        SystemLibrary.Ensure<T3>();
        SystemLibrary.Ensure<T4>();
        SystemLibrary.Ensure<T5>();
        SystemLibrary.Ensure<T6>();
        SystemLibrary.Ensure<T7>();
        SystemLibrary.Ensure<T8>();
        SystemLibrary.Ensure<T9>();
    }
}

public class SystemUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>
    : TypeUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>, ISystemUnion
    where T1 : ISystem, new()
    where T2 : ISystem, new()
    where T3 : ISystem, new()
    where T4 : ISystem, new()
    where T5 : ISystem, new()
    where T6 : ISystem, new()
    where T7 : ISystem, new()
    where T8 : ISystem, new()
    where T9 : ISystem, new()
    where T10 : ISystem, new()
{
    static SystemUnion()
    {
        SystemLibrary.Ensure<T1>();
        SystemLibrary.Ensure<T2>();
        SystemLibrary.Ensure<T3>();
        SystemLibrary.Ensure<T4>();
        SystemLibrary.Ensure<T5>();
        SystemLibrary.Ensure<T6>();
        SystemLibrary.Ensure<T7>();
        SystemLibrary.Ensure<T8>();
        SystemLibrary.Ensure<T9>();
        SystemLibrary.Ensure<T10>();
    }
}

public class SystemUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>
    : TypeUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>, ISystemUnion
    where T1 : ISystem, new()
    where T2 : ISystem, new()
    where T3 : ISystem, new()
    where T4 : ISystem, new()
    where T5 : ISystem, new()
    where T6 : ISystem, new()
    where T7 : ISystem, new()
    where T8 : ISystem, new()
    where T9 : ISystem, new()
    where T10 : ISystem, new()
    where T11 : ISystem, new()
{
    static SystemUnion()
    {
        SystemLibrary.Ensure<T1>();
        SystemLibrary.Ensure<T2>();
        SystemLibrary.Ensure<T3>();
        SystemLibrary.Ensure<T4>();
        SystemLibrary.Ensure<T5>();
        SystemLibrary.Ensure<T6>();
        SystemLibrary.Ensure<T7>();
        SystemLibrary.Ensure<T8>();
        SystemLibrary.Ensure<T9>();
        SystemLibrary.Ensure<T10>();
        SystemLibrary.Ensure<T11>();
    }
}

public class SystemUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>
    : TypeUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>, ISystemUnion
    where T1 : ISystem, new()
    where T2 : ISystem, new()
    where T3 : ISystem, new()
    where T4 : ISystem, new()
    where T5 : ISystem, new()
    where T6 : ISystem, new()
    where T7 : ISystem, new()
    where T8 : ISystem, new()
    where T9 : ISystem, new()
    where T10 : ISystem, new()
    where T11 : ISystem, new()
    where T12 : ISystem, new()
{
    static SystemUnion()
    {
        SystemLibrary.Ensure<T1>();
        SystemLibrary.Ensure<T2>();
        SystemLibrary.Ensure<T3>();
        SystemLibrary.Ensure<T4>();
        SystemLibrary.Ensure<T5>();
        SystemLibrary.Ensure<T6>();
        SystemLibrary.Ensure<T7>();
        SystemLibrary.Ensure<T8>();
        SystemLibrary.Ensure<T9>();
        SystemLibrary.Ensure<T10>();
        SystemLibrary.Ensure<T11>();
        SystemLibrary.Ensure<T12>();
    }
}

public class SystemUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>
    : TypeUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>, ISystemUnion
    where T1 : ISystem, new()
    where T2 : ISystem, new()
    where T3 : ISystem, new()
    where T4 : ISystem, new()
    where T5 : ISystem, new()
    where T6 : ISystem, new()
    where T7 : ISystem, new()
    where T8 : ISystem, new()
    where T9 : ISystem, new()
    where T10 : ISystem, new()
    where T11 : ISystem, new()
    where T12 : ISystem, new()
    where T13 : ISystem, new()
{
    static SystemUnion()
    {
        SystemLibrary.Ensure<T1>();
        SystemLibrary.Ensure<T2>();
        SystemLibrary.Ensure<T3>();
        SystemLibrary.Ensure<T4>();
        SystemLibrary.Ensure<T5>();
        SystemLibrary.Ensure<T6>();
        SystemLibrary.Ensure<T7>();
        SystemLibrary.Ensure<T8>();
        SystemLibrary.Ensure<T9>();
        SystemLibrary.Ensure<T10>();
        SystemLibrary.Ensure<T11>();
        SystemLibrary.Ensure<T12>();
        SystemLibrary.Ensure<T13>();
    }
}

public class SystemUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>
    : TypeUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>, ISystemUnion
    where T1 : ISystem, new()
    where T2 : ISystem, new()
    where T3 : ISystem, new()
    where T4 : ISystem, new()
    where T5 : ISystem, new()
    where T6 : ISystem, new()
    where T7 : ISystem, new()
    where T8 : ISystem, new()
    where T9 : ISystem, new()
    where T10 : ISystem, new()
    where T11 : ISystem, new()
    where T12 : ISystem, new()
    where T13 : ISystem, new()
    where T14 : ISystem, new()
{
    static SystemUnion()
    {
        SystemLibrary.Ensure<T1>();
        SystemLibrary.Ensure<T2>();
        SystemLibrary.Ensure<T3>();
        SystemLibrary.Ensure<T4>();
        SystemLibrary.Ensure<T5>();
        SystemLibrary.Ensure<T6>();
        SystemLibrary.Ensure<T7>();
        SystemLibrary.Ensure<T8>();
        SystemLibrary.Ensure<T9>();
        SystemLibrary.Ensure<T10>();
        SystemLibrary.Ensure<T11>();
        SystemLibrary.Ensure<T12>();
        SystemLibrary.Ensure<T13>();
        SystemLibrary.Ensure<T14>();
    }
}

public class SystemUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15>
    : TypeUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15>, ISystemUnion
    where T1 : ISystem, new()
    where T2 : ISystem, new()
    where T3 : ISystem, new()
    where T4 : ISystem, new()
    where T5 : ISystem, new()
    where T6 : ISystem, new()
    where T7 : ISystem, new()
    where T8 : ISystem, new()
    where T9 : ISystem, new()
    where T10 : ISystem, new()
    where T11 : ISystem, new()
    where T12 : ISystem, new()
    where T13 : ISystem, new()
    where T14 : ISystem, new()
    where T15 : ISystem, new()
{
    static SystemUnion()
    {
        SystemLibrary.Ensure<T1>();
        SystemLibrary.Ensure<T2>();
        SystemLibrary.Ensure<T3>();
        SystemLibrary.Ensure<T4>();
        SystemLibrary.Ensure<T5>();
        SystemLibrary.Ensure<T6>();
        SystemLibrary.Ensure<T7>();
        SystemLibrary.Ensure<T8>();
        SystemLibrary.Ensure<T9>();
        SystemLibrary.Ensure<T10>();
        SystemLibrary.Ensure<T11>();
        SystemLibrary.Ensure<T12>();
        SystemLibrary.Ensure<T13>();
        SystemLibrary.Ensure<T14>();
        SystemLibrary.Ensure<T15>();
    }
}

public class SystemUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16>
    : TypeUnion<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16>, ISystemUnion
    where T1 : ISystem, new()
    where T2 : ISystem, new()
    where T3 : ISystem, new()
    where T4 : ISystem, new()
    where T5 : ISystem, new()
    where T6 : ISystem, new()
    where T7 : ISystem, new()
    where T8 : ISystem, new()
    where T9 : ISystem, new()
    where T10 : ISystem, new()
    where T11 : ISystem, new()
    where T12 : ISystem, new()
    where T13 : ISystem, new()
    where T14 : ISystem, new()
    where T15 : ISystem, new()
    where T16 : ISystem, new()
{
    static SystemUnion()
    {
        SystemLibrary.Ensure<T1>();
        SystemLibrary.Ensure<T2>();
        SystemLibrary.Ensure<T3>();
        SystemLibrary.Ensure<T4>();
        SystemLibrary.Ensure<T5>();
        SystemLibrary.Ensure<T6>();
        SystemLibrary.Ensure<T7>();
        SystemLibrary.Ensure<T8>();
        SystemLibrary.Ensure<T9>();
        SystemLibrary.Ensure<T10>();
        SystemLibrary.Ensure<T11>();
        SystemLibrary.Ensure<T12>();
        SystemLibrary.Ensure<T13>();
        SystemLibrary.Ensure<T14>();
        SystemLibrary.Ensure<T15>();
        SystemLibrary.Ensure<T16>();
    }
}