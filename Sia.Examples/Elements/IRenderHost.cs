namespace Sia_Examples;

public interface IRenderHost<TView>
    where TView : struct, IEquatable<TView>
{
    public void Upsert(in TView view);

    public void Remove(in TView view);
}
