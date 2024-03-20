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
        var entity = new Transform
        {
            Position = Vector3.Zero,
            Rotation = Quaternion.Identity,
            Scale = 1.0f
        };

        // Act
        var ptr = (IntPtr)Unsafe.AsPointer(ref entity);
        var desc = EntityDescriptor.Get<Transform>();

        // Assert
        Assert.Equal(0, desc.GetOffset<Vector3>());
        Assert.Equal(Vector3.Zero, Unsafe.AsRef<Vector3>((void*)(ptr + desc.GetOffset<Vector3>())));

        Assert.Equal(4 * 3, desc.GetOffset<Quaternion>());
        Assert.Equal(Quaternion.Identity, Unsafe.AsRef<Quaternion>((void*)(ptr + desc.GetOffset<Quaternion>())));

        Assert.Equal(4 * 3 + 4 * 4, desc.GetOffset<float>());
        Assert.Equal(1.0f, Unsafe.AsRef<float>((void*)(ptr + desc.GetOffset<float>())));
    }
}