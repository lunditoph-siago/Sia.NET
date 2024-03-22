namespace Sia;

public static class WorldEvents
{
    public class Add : SingletonEvent<Add>;
    public class Remove : SingletonEvent<Remove>;

    public class Add<TComponent> : SingletonEvent<Add<TComponent>>;
    public class Remove<TComponent> : SingletonEvent<Remove<TComponent>>;
}