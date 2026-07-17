namespace Sia;

public record struct EntityId(int Value)
{
    private static int s_acc;

    public static EntityId Create()
    {
        int value;
        do {
            value = Interlocked.Increment(ref s_acc);
        } while (value == 0);
        return new(value);
    }

    internal static int Reserve(int count)
    {
        var end = Interlocked.Add(ref s_acc, count);
        var start = end - count + 1;
        return start <= 0 && end >= 0
            ? Reserve(count)
            : start;
    }

    public readonly override string ToString()
        => Value.ToString();
}
