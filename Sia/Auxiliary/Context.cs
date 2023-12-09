namespace Sia;

public static class Context<T>
{
    public static T? Current => s_current.Value;

    private static readonly ThreadLocal<T?> s_current = new();

    public static void With(T value, Action action)
    {
        var prev = s_current.Value;
        s_current.Value = value;

        try {
            action();
        }
        finally {
            s_current.Value = prev;
        }
    }
}