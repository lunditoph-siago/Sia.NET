using System.Numerics;
using Sia.Tests.Components;

namespace Sia.Tests.Entities;

public class StorageEntityHostTests
{
    public static List<object[]> StorageEntityHostTestData =>
    [
        [new ArrayBufferStorage<WithId<Transform>>(512)],
        [new SparseBufferStorage<WithId<Transform>>(512)],
        [new HashBufferStorage<WithId<Transform>>()]
    ];

    [Theory]
    [MemberData(nameof(StorageEntityHostTestData))]
    public void StorageEntityHost_Insert_Test<TStorage>(TStorage storage)
        where TStorage : class, IStorage<WithId<Transform>>
    {
        // Arrange
        var factory = new StorageEntityHost<Transform, TStorage>(storage);

        // Act
        var e1 = factory.Create(Transform.Default);
        var e2 = factory.Create();
        var e3 = factory.Create();

        // Assert
        Assert.Equal(Vector3.Zero, e1.Get<Vector3>());
        Assert.Equal(Quaternion.Identity, e1.Get<Quaternion>());
        Assert.Equal(1.0f, e1.Get<float>());
        Assert.Equal(Vector3.Zero, e2.Get<Vector3>());
        Assert.Equal(Vector3.Zero, e3.Get<Vector3>());
    }
}