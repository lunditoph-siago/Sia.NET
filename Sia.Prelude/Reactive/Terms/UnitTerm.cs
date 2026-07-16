namespace Sia.Reactive;

public readonly record struct UnitTerm : ITerm<UnitTerm>
{
    public static int SlotCount => 0;

    public static void Mount(in UnitTerm self, ref GraphContext ctx) {}

    public static void Reconcile(in UnitTerm prev, in UnitTerm next, ref GraphContext ctx) {}
}
