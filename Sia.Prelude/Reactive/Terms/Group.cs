namespace Sia.Reactive;

public readonly record struct Group<T1, T2>(T1 Item1, T2 Item2)
    : ITerm<Group<T1, T2>>
    where T1 : struct, ITerm<T1>
    where T2 : struct, ITerm<T2>
{
    public static int SlotCount => T1.SlotCount + T2.SlotCount;

    public static void Mount(in Group<T1, T2> self, ref GraphContext ctx)
    {
        T1.Mount(self.Item1, ref ctx);
        T2.Mount(self.Item2, ref ctx);
    }

    public static void Reconcile(
        in Group<T1, T2> prev, in Group<T1, T2> next, ref GraphContext ctx)
    {
        T1.Reconcile(prev.Item1, next.Item1, ref ctx);
        T2.Reconcile(prev.Item2, next.Item2, ref ctx);
    }
}

public readonly record struct Group<T1, T2, T3>(T1 Item1, T2 Item2, T3 Item3)
    : ITerm<Group<T1, T2, T3>>
    where T1 : struct, ITerm<T1>
    where T2 : struct, ITerm<T2>
    where T3 : struct, ITerm<T3>
{
    public static int SlotCount => T1.SlotCount + T2.SlotCount + T3.SlotCount;

    public static void Mount(in Group<T1, T2, T3> self, ref GraphContext ctx)
    {
        T1.Mount(self.Item1, ref ctx);
        T2.Mount(self.Item2, ref ctx);
        T3.Mount(self.Item3, ref ctx);
    }

    public static void Reconcile(
        in Group<T1, T2, T3> prev, in Group<T1, T2, T3> next, ref GraphContext ctx)
    {
        T1.Reconcile(prev.Item1, next.Item1, ref ctx);
        T2.Reconcile(prev.Item2, next.Item2, ref ctx);
        T3.Reconcile(prev.Item3, next.Item3, ref ctx);
    }
}

public readonly record struct Group<T1, T2, T3, T4>(T1 Item1, T2 Item2, T3 Item3, T4 Item4)
    : ITerm<Group<T1, T2, T3, T4>>
    where T1 : struct, ITerm<T1>
    where T2 : struct, ITerm<T2>
    where T3 : struct, ITerm<T3>
    where T4 : struct, ITerm<T4>
{
    public static int SlotCount => T1.SlotCount + T2.SlotCount + T3.SlotCount + T4.SlotCount;

    public static void Mount(in Group<T1, T2, T3, T4> self, ref GraphContext ctx)
    {
        T1.Mount(self.Item1, ref ctx);
        T2.Mount(self.Item2, ref ctx);
        T3.Mount(self.Item3, ref ctx);
        T4.Mount(self.Item4, ref ctx);
    }

    public static void Reconcile(
        in Group<T1, T2, T3, T4> prev, in Group<T1, T2, T3, T4> next,
        ref GraphContext ctx)
    {
        T1.Reconcile(prev.Item1, next.Item1, ref ctx);
        T2.Reconcile(prev.Item2, next.Item2, ref ctx);
        T3.Reconcile(prev.Item3, next.Item3, ref ctx);
        T4.Reconcile(prev.Item4, next.Item4, ref ctx);
    }
}

public readonly record struct Group<T1, T2, T3, T4, T5>(
    T1 Item1, T2 Item2, T3 Item3, T4 Item4, T5 Item5)
    : ITerm<Group<T1, T2, T3, T4, T5>>
    where T1 : struct, ITerm<T1>
    where T2 : struct, ITerm<T2>
    where T3 : struct, ITerm<T3>
    where T4 : struct, ITerm<T4>
    where T5 : struct, ITerm<T5>
{
    public static int SlotCount =>
        T1.SlotCount + T2.SlotCount + T3.SlotCount + T4.SlotCount + T5.SlotCount;

    public static void Mount(in Group<T1, T2, T3, T4, T5> self, ref GraphContext ctx)
    {
        T1.Mount(self.Item1, ref ctx);
        T2.Mount(self.Item2, ref ctx);
        T3.Mount(self.Item3, ref ctx);
        T4.Mount(self.Item4, ref ctx);
        T5.Mount(self.Item5, ref ctx);
    }

    public static void Reconcile(
        in Group<T1, T2, T3, T4, T5> prev, in Group<T1, T2, T3, T4, T5> next,
        ref GraphContext ctx)
    {
        T1.Reconcile(prev.Item1, next.Item1, ref ctx);
        T2.Reconcile(prev.Item2, next.Item2, ref ctx);
        T3.Reconcile(prev.Item3, next.Item3, ref ctx);
        T4.Reconcile(prev.Item4, next.Item4, ref ctx);
        T5.Reconcile(prev.Item5, next.Item5, ref ctx);
    }
}

public readonly record struct Group<T1, T2, T3, T4, T5, T6>(
    T1 Item1, T2 Item2, T3 Item3, T4 Item4, T5 Item5, T6 Item6)
    : ITerm<Group<T1, T2, T3, T4, T5, T6>>
    where T1 : struct, ITerm<T1>
    where T2 : struct, ITerm<T2>
    where T3 : struct, ITerm<T3>
    where T4 : struct, ITerm<T4>
    where T5 : struct, ITerm<T5>
    where T6 : struct, ITerm<T6>
{
    public static int SlotCount =>
        T1.SlotCount + T2.SlotCount + T3.SlotCount + T4.SlotCount + T5.SlotCount + T6.SlotCount;

    public static void Mount(in Group<T1, T2, T3, T4, T5, T6> self, ref GraphContext ctx)
    {
        T1.Mount(self.Item1, ref ctx);
        T2.Mount(self.Item2, ref ctx);
        T3.Mount(self.Item3, ref ctx);
        T4.Mount(self.Item4, ref ctx);
        T5.Mount(self.Item5, ref ctx);
        T6.Mount(self.Item6, ref ctx);
    }

    public static void Reconcile(
        in Group<T1, T2, T3, T4, T5, T6> prev, in Group<T1, T2, T3, T4, T5, T6> next,
        ref GraphContext ctx)
    {
        T1.Reconcile(prev.Item1, next.Item1, ref ctx);
        T2.Reconcile(prev.Item2, next.Item2, ref ctx);
        T3.Reconcile(prev.Item3, next.Item3, ref ctx);
        T4.Reconcile(prev.Item4, next.Item4, ref ctx);
        T5.Reconcile(prev.Item5, next.Item5, ref ctx);
        T6.Reconcile(prev.Item6, next.Item6, ref ctx);
    }
}

public readonly record struct Group<T1, T2, T3, T4, T5, T6, T7>(
    T1 Item1, T2 Item2, T3 Item3, T4 Item4, T5 Item5, T6 Item6, T7 Item7)
    : ITerm<Group<T1, T2, T3, T4, T5, T6, T7>>
    where T1 : struct, ITerm<T1>
    where T2 : struct, ITerm<T2>
    where T3 : struct, ITerm<T3>
    where T4 : struct, ITerm<T4>
    where T5 : struct, ITerm<T5>
    where T6 : struct, ITerm<T6>
    where T7 : struct, ITerm<T7>
{
    public static int SlotCount =>
        T1.SlotCount + T2.SlotCount + T3.SlotCount + T4.SlotCount
        + T5.SlotCount + T6.SlotCount + T7.SlotCount;

    public static void Mount(
        in Group<T1, T2, T3, T4, T5, T6, T7> self, ref GraphContext ctx)
    {
        T1.Mount(self.Item1, ref ctx);
        T2.Mount(self.Item2, ref ctx);
        T3.Mount(self.Item3, ref ctx);
        T4.Mount(self.Item4, ref ctx);
        T5.Mount(self.Item5, ref ctx);
        T6.Mount(self.Item6, ref ctx);
        T7.Mount(self.Item7, ref ctx);
    }

    public static void Reconcile(
        in Group<T1, T2, T3, T4, T5, T6, T7> prev,
        in Group<T1, T2, T3, T4, T5, T6, T7> next,
        ref GraphContext ctx)
    {
        T1.Reconcile(prev.Item1, next.Item1, ref ctx);
        T2.Reconcile(prev.Item2, next.Item2, ref ctx);
        T3.Reconcile(prev.Item3, next.Item3, ref ctx);
        T4.Reconcile(prev.Item4, next.Item4, ref ctx);
        T5.Reconcile(prev.Item5, next.Item5, ref ctx);
        T6.Reconcile(prev.Item6, next.Item6, ref ctx);
        T7.Reconcile(prev.Item7, next.Item7, ref ctx);
    }
}

public readonly record struct Group<T1, T2, T3, T4, T5, T6, T7, T8>(
    T1 Item1, T2 Item2, T3 Item3, T4 Item4, T5 Item5, T6 Item6, T7 Item7, T8 Item8)
    : ITerm<Group<T1, T2, T3, T4, T5, T6, T7, T8>>
    where T1 : struct, ITerm<T1>
    where T2 : struct, ITerm<T2>
    where T3 : struct, ITerm<T3>
    where T4 : struct, ITerm<T4>
    where T5 : struct, ITerm<T5>
    where T6 : struct, ITerm<T6>
    where T7 : struct, ITerm<T7>
    where T8 : struct, ITerm<T8>
{
    public static int SlotCount =>
        T1.SlotCount + T2.SlotCount + T3.SlotCount + T4.SlotCount
        + T5.SlotCount + T6.SlotCount + T7.SlotCount + T8.SlotCount;

    public static void Mount(
        in Group<T1, T2, T3, T4, T5, T6, T7, T8> self, ref GraphContext ctx)
    {
        T1.Mount(self.Item1, ref ctx);
        T2.Mount(self.Item2, ref ctx);
        T3.Mount(self.Item3, ref ctx);
        T4.Mount(self.Item4, ref ctx);
        T5.Mount(self.Item5, ref ctx);
        T6.Mount(self.Item6, ref ctx);
        T7.Mount(self.Item7, ref ctx);
        T8.Mount(self.Item8, ref ctx);
    }

    public static void Reconcile(
        in Group<T1, T2, T3, T4, T5, T6, T7, T8> prev,
        in Group<T1, T2, T3, T4, T5, T6, T7, T8> next,
        ref GraphContext ctx)
    {
        T1.Reconcile(prev.Item1, next.Item1, ref ctx);
        T2.Reconcile(prev.Item2, next.Item2, ref ctx);
        T3.Reconcile(prev.Item3, next.Item3, ref ctx);
        T4.Reconcile(prev.Item4, next.Item4, ref ctx);
        T5.Reconcile(prev.Item5, next.Item5, ref ctx);
        T6.Reconcile(prev.Item6, next.Item6, ref ctx);
        T7.Reconcile(prev.Item7, next.Item7, ref ctx);
        T8.Reconcile(prev.Item8, next.Item8, ref ctx);
    }
}
