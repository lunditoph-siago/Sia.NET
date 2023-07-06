namespace Sia;

public static class WorldEvents
{
    public record Add : SingletonEvent<Add>;
    public record Remove : SingletonEvent<Remove>;
}