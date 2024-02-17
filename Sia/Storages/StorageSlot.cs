namespace Sia;

public record struct StorageSlot(int Index, short Version)
{
    internal ushort Extra;
}