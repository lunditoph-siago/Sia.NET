namespace Sia;

public static class WorldEvents
{
    public class Add : SingletonEvent<Add> {}
    public class Remove : SingletonEvent<Remove> {}
}