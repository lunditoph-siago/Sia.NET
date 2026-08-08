using Sia.Reactive;

namespace Sia_Examples;

public readonly record struct RenderEffect<TView>(
    IRenderHost<TView> Host,
    TView View) : IEffect<RenderEffect<TView>>
    where TView : struct, IEquatable<TView>
{
    public static void Mount(in RenderEffect<TView> self)
        => self.Host.Upsert(self.View);

    public static void Reconcile(
        in RenderEffect<TView> previous,
        in RenderEffect<TView> next)
    {
        if (!ReferenceEquals(previous.Host, next.Host)) {
            previous.Host.Remove(previous.View);
            next.Host.Upsert(next.View);
        } else if (!previous.View.Equals(next.View)) {
            next.Host.Upsert(next.View);
        }
    }

    public static void Unmount(in RenderEffect<TView> self)
        => self.Host.Remove(self.View);
}
