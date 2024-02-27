namespace Sia;

public static class Context<T>
{
    public static T? Current {
        get => s_current.Value;
        set => s_current.Value = value;
    }

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

public static class Context
{
    public static T Get<T>()
        => Context<T>.Current
            ?? throw new NotSupportedException("Context not found: " + typeof(T));
}