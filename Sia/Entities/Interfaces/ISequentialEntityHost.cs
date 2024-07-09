namespace Sia;

public interface ISequentialEntityHost : IEntityHost
{
    public Span<byte> Bytes { get; }
}