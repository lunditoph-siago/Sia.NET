using System.Numerics;
using System.Runtime.CompilerServices;
using Sia.Tests.Components;

namespace Sia.Tests.Entities;

public class EntityDescriptorTests
{
    [Fact]
    public unsafe void EntityDescriptor_Test()
    {
        // Arrange
        var entity = Transform.BakedEntity;

        // Act
        var ptr = (IntPtr)Unsafe.AsPointer(ref entity);
        var desc = EntityDescriptor.Get<HList<Position, HList<Rotation, HList<Scale, EmptyHList>>>>();

        // Assert
        Assert.Equal(0, desc.GetOffset<Position>());
        Assert.Equal(Vector3.Zero, Unsafe.AsRef<Position>((void*)(ptr + desc.GetOffset<Position>())));

        Assert.Equal(4 * 3, desc.GetOffset<Rotation>());
        Assert.Equal(Quaternion.Identity, Unsafe.AsRef<Rotation>((void*)(ptr + desc.GetOffset<Rotation>())));

        Assert.Equal(4 * 3 + 4 * 4, desc.GetOffset<Scale>());
        Assert.Equal(1.0f, Unsafe.AsRef<Scale>((void*)(ptr + desc.GetOffset<Scale>())));
    }
}