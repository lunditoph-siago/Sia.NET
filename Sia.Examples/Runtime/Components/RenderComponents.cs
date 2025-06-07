using System.Drawing;
using System.Numerics;
using Sia;

namespace Sia_Examples.Runtime.Components;

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

public partial record struct UIText(
    [Sia] string Content,
    [Sia] Color TextColor,
    [Sia] float FontSize,
    [Sia] bool IsVisible)
{
    public UIText() : this(string.Empty, Color.White, 12f, true) { }
}

public partial record struct UIPanel(
    [Sia] Vector2 Size,
    [Sia] Color BackgroundColor,
    [Sia] bool IsVisible)
{
    public UIPanel() : this(Vector2.One * 100, Color.Gray, true) { }
}

public partial record struct RenderLayer(
    [Sia] int Layer,
    [Sia] bool IsVisible)
{
    public RenderLayer() : this(0, true) { }
}

public partial record struct InputReceiver(
    [Sia] bool CanReceiveKeyboard,
    [Sia] bool CanReceiveMouse,
    [Sia] bool IsEnabled)
{
    public InputReceiver() : this(false, false, true) { }
}

public partial record struct Clickable(
    [Sia] Vector2 Size,
    [Sia] bool IsHovered,
    [Sia] bool IsPressed)
{
    public Clickable() : this(Vector2.One * 100, false, false) { }

    public readonly record struct Click : IEvent;
    public readonly record struct Hover : IEvent;
    public readonly record struct Leave : IEvent;
}

public partial record struct ScrollContainer(
    [Sia] Vector2 ContentSize,
    [Sia] Vector2 ViewportSize,
    [Sia] Vector2 ScrollOffset)
{
    public ScrollContainer() : this(Vector2.Zero, Vector2.One * 100, Vector2.Zero) { }
}