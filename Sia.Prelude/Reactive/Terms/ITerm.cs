namespace Sia.Reactive;

public interface ITerm<TSelf>
    where TSelf : struct, ITerm<TSelf>
{
    static abstract int SlotCount { get; }

    static abstract void Mount(in TSelf self, ref GraphContext ctx);

    static abstract void Reconcile(in TSelf prev, in TSelf next, ref GraphContext ctx);
}
