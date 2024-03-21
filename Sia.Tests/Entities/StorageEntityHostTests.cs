using System.Numerics;
using TransformEntity = Sia.HList<System.Numerics.Vector3, Sia.HList<System.Numerics.Quaternion, Sia.HList<float, Sia.EmptyHList>>>;

namespace Sia.Tests.Entities;

public class StorageEntityHostTests
{

    public static List<object[]> StorageEntityHostTestData =>
    [
        [new ArrayBufferStorage<HList<Identity, TransformEntity>>(512)],
        [new SparseBufferStorage<HList<Identity, TransformEntity>>(512)],
        [new HashBufferStorage<HList<Identity, TransformEntity>>()]
    ];

    [Theory]
    [MemberData(nameof(StorageEntityHostTestData))]
    public void StorageEntityHost_Insert_Test<TStorage>(TStorage storage)
        where TStorage : class, IStorage<HList<Identity, TransformEntity>>
    {
        // Arrange
        var factory = new StorageEntityHost<TransformEntity, TStorage>(storage);

        // Act
        var e1 = factory.Create(Components.Transform.Baked);
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