using System.Numerics;
using Sia.Tests.Components;
using TransformEntity = Sia.HList<Sia.Tests.Components.Position, Sia.HList<Sia.Tests.Components.Rotation, Sia.HList<Sia.Tests.Components.Scale, Sia.EmptyHList>>>;

namespace Sia.Tests.Entities;

public class StorageEntityHostTests
{

    public static List<object[]> StorageEntityHostTestData =>
    [
        [new ArrayBufferStorage<HList<Entity, TransformEntity>>(512)],
        [new SparseBufferStorage<HList<Entity, TransformEntity>>(512)],
        [new HashBufferStorage<HList<Entity, TransformEntity>>()]
    ];

    [Theory]
    [MemberData(nameof(StorageEntityHostTestData))]
    public void StorageEntityHost_Insert_Test<TStorage>(TStorage storage)
        where TStorage : class, IStorage<HList<Entity, TransformEntity>>
    {
        // Arrange
        var factory = new StorageEntityHost<TransformEntity, TStorage>(storage);

        // Act
        var e1 = factory.Create(Components.Transform.BakedEntity);
        var e2 = factory.Create();
        var e3 = factory.Create();

        // Assert
        Assert.Equal(Vector3.Zero, e1.Get<Position>());
        Assert.Equal(Quaternion.Identity, e1.Get<Rotation>());
        Assert.Equal(1.0f, e1.Get<Scale>());
        Assert.Equal(Vector3.Zero, e2.Get<Position>());
        Assert.Equal(Vector3.Zero, e3.Get<Position>());
    }
}