namespace Sia;

public static class Context<T>
{
    public static T? Current {
        get => s_current;
        set => s_current = value;
    }

    [ThreadStatic]
    private static T? s_current;

    public static void With(T value, Action action)
    {
        var prev = s_current;
        s_current = value;

        try {
            action();
        } 
        finally {
            s_current = prev;
        }
    }
}

public static class Context
{
    public static T Get<T>()
        => Context<T>.Current
            ?? throw new NotSupportedException("Context not found: " + typeof(T));
}