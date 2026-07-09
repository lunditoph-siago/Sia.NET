namespace Sia.Reactive;

public readonly record struct StateRef<TState>(World World, Entity Cell)
    where TState : struct
{
    public ref TState Value => ref Cell.Get<TState>();

    public void Set(in TState value)
    {
        Cell.Get<TState>() = value;
        Notify();
    }

    public void Notify()
        => World.Send(Cell, CellEvents.Invalidate.Instance);
}
