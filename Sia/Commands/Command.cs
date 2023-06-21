namespace Sia;

public abstract record Command : ICommand
{
    public virtual void Dispose()
    {
    }
}