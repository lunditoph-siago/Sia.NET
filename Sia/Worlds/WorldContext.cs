namespace Sia;

public readonly record struct WorldContext(
    World World,
    CancellationToken Cancellation = default)
{
    public readonly void ThrowIfCancellationRequested()
        => Cancellation.ThrowIfCancellationRequested();
}
