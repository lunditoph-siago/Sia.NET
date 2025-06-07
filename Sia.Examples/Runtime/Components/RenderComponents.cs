using System.Numerics;

namespace Sia.Examples.Runtime.Components;

public partial record struct Transform(
    [Sia] Vector3 Position,
    [Sia] Quaternion Rotation,
    [Sia] Vector3 Scale)
{
    public Transform() : this(Vector3.Zero, Quaternion.Identity, Vector3.One) { }

    public readonly Matrix4x4 ToMatrix() =>
        Matrix4x4.CreateScale(Scale) *
        Matrix4x4.CreateFromQuaternion(Rotation) *
        Matrix4x4.CreateTranslation(Position);
}

public partial record struct Camera(
    [Sia] Matrix4x4 ViewMatrix,
    [Sia] Matrix4x4 ProjectionMatrix,
    [Sia] bool IsActive)
{
    public Camera() : this(Matrix4x4.Identity, Matrix4x4.Identity, true) { }

    public static Camera CreateOrthographic(float width, float height, float near, float far) => new()
    {
        ViewMatrix = Matrix4x4.Identity,
        ProjectionMatrix = Matrix4x4.CreateOrthographicOffCenter(0, width, 0, height, near, far),
        IsActive = true
    };
}

public partial record struct RenderLayer(
    [Sia] int Layer,
    [Sia] bool IsVisible)
{
    public RenderLayer() : this(0, true) { }
}