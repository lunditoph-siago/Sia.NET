namespace Sia;

public static class WorldCommands
{
    public record Add : SingletonCommand<Add>;
    public record Remove : SingletonCommand<Remove>;
}