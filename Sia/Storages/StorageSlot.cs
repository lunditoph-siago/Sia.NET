namespace Sia;

public record struct StorageSlot(int Index, int Id, short Version)
{
    private static int s_idAcc;
    public static int NewId() => Interlocked.Increment(ref s_idAcc);
}