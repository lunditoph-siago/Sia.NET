namespace Sia;

public record struct EntityId(int Value)
{
    private static int s_acc;

    public static EntityId Create()
        => new(Interlocked.Increment(ref s_acc));

    public readonly override string ToString()
        => Value.ToString();
}