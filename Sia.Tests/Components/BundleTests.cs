using System.Numerics;

namespace Sia.Tests.Components;

public class BundleTests
{
    public static List<object[]> BundleTestData =>
    [
        [
            new Transform { Position = Vector3.Zero, Rotation = Quaternion.Identity, Scale = 1.0f },
            new GameObject(new Sid<ObjectId>(0), "test")
        ]
    ];

    [Theory]
    [MemberData(nameof(BundleTestData))]
    public void Bundle_AddBundle_Test(Transform transform, GameObject gameObject)
    {
        using var fixture = new WorldFixture();

        var entityRef = fixture.World.CreateInArrayHost(HList.Create(0))
            .AddBundle(transform)
            .AddBundle(gameObject);

        Assert.Equal(transform.Position, entityRef.Get<Position>());
        Assert.Equal(transform.Rotation, entityRef.Get<Rotation>());
        Assert.Equal(transform.Scale, entityRef.Get<Scale>());
        Assert.Equal(gameObject.Name, entityRef.Get<Name>());
    }

    [Theory]
    [MemberData(nameof(BundleTestData))]
    public void Bundle_AddDynBundle_Test(Transform transform, GameObject gameObject)
    {
        using var fixture = new WorldFixture();

        var entityRef = fixture.World.CreateInArrayHost(HList.Create(0))
            .AddBundle(
                new DynBundle()
                    .AddBundle(transform)
                    .AddBundle(gameObject));

        Assert.Equal(transform.Position, entityRef.Get<Position>());
        Assert.Equal(transform.Rotation, entityRef.Get<Rotation>());
        Assert.Equal(transform.Scale, entityRef.Get<Scale>());
        Assert.Equal(gameObject.Name, entityRef.Get<Name>());
    }
}