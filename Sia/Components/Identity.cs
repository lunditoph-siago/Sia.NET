namespace Sia;

public readonly record struct Identity(long Value)
{
    private static long s_acc;
    public static Identity Create() => new(Interlocked.Increment(ref s_acc));
}