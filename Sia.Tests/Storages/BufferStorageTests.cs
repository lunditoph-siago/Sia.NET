namespace Sia.Tests.Storages;

public class BufferStorageTests
{
    public static List<object[]> BufferStorageTestData =>
    [
        [new ArrayBufferStorage<int>(5120)],
        [new SparseBufferStorage<int>(5120)],
        [new HashBufferStorage<int>()]
    ];

    [Theory]
    [MemberData(nameof(BufferStorageTestData))]
    public void BufferStorage_Test<TStorage>(TStorage storage)
        where TStorage : class, IStorage<int>
    {
        var pointers = new List<Pointer<int>>();
            
        for (var c = 0; c < 10; c++) {
            if (Random.Shared.NextSingle() < 0.5) {
                var count = Random.Shared.Next(1, 30);
                for (var i = 0; i < count; ++i) {
                    pointers.Add(storage.Allocate());
                }
            }
            else {
                while (pointers.Count > 0) {
                    var index = Random.Shared.Next(0, pointers.Count);
                    pointers[index].Dispose();
                    pointers.RemoveAt(index);
                }
            }
        }

        while (pointers.Count > 0) {
            var index = Random.Shared.Next(0, pointers.Count);
            pointers[index].Dispose();
            pointers.RemoveAt(index);
        }
    }
}