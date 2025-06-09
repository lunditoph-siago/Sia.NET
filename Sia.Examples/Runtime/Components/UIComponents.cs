using System.Drawing;
using System.Numerics;

namespace Sia.Examples.Runtime.Components;

public sealed class UIHierarchyTag { }

public partial record struct UIElement(
    [Sia] Vector2 Position,
    [Sia] Vector2 Size,
    [Sia] bool IsVisible,
    [Sia] bool IsInteractable,
    [Sia] int Layer)
{
    public UIElement() : this(Vector2.Zero, Vector2.One * 100, true, true, 0) { }

    public readonly bool Contains(Vector2 point) =>
        point.X >= Position.X && point.X <= Position.X + Size.X &&
        point.Y >= Position.Y && point.Y <= Position.Y + Size.Y;
}

public interface IUIEvent : IEvent
{
    Entity Target { get; }
    Vector2 Position { get; }
}

public enum MouseButton : byte
{
    Left = 0,
    Right = 1,
    Middle = 2
}

public readonly record struct UIClickEvent(Entity Target, Vector2 Position, MouseButton Button) : IUIEvent;

public readonly record struct UIHoverEnterEvent(Entity Target, Vector2 Position) : IUIEvent;

public readonly record struct UIHoverExitEvent(Entity Target, Vector2 Position) : IUIEvent;

public readonly record struct UIScrollEvent(Entity Target, Vector2 Position, Vector2 ScrollDelta) : IUIEvent;

public partial record struct UIInteractionState(
    [Sia] bool IsHovered,
    [Sia] bool IsPressed,
    [Sia] MouseButton PressedButton)
{
    public UIInteractionState() : this(false, false, MouseButton.Left) { }
}

public partial record struct UIButton(
    [Sia] Color NormalColor,
    [Sia] Color HoverColor,
    [Sia] Color PressedColor,
    [Sia] bool IsEnabled)
{
    public UIButton() : this(Color.Gray, Color.LightGray, Color.DarkGray, true) { }
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
    [Sia] Color BackgroundColor,
    [Sia] bool IsVisible)
{
    public UIPanel() : this(Color.Gray, true) { }
}

public partial record struct UIEventListener(
    [Sia] bool ListenToClick,
    [Sia] bool ListenToHover,
    [Sia] bool ListenToPress,
    [Sia] bool ListenToScroll,
    [Sia] bool IsEnabled)
{
    public UIEventListener() : this(true, true, true, true, true) { }
}

public partial record struct UIScrollable(
    [Sia] Vector2 ContentSize,
    [Sia] Vector2 ScrollOffset,
    [Sia] Vector2 ScrollSpeed,
    [Sia] bool EnableHorizontal,
    [Sia] bool EnableVertical,
    [Sia] bool ShowScrollbars,
    [Sia] bool IsEnabled)
{
    public UIScrollable() : this(Vector2.Zero, Vector2.Zero, new Vector2(20f), false, true, true, true) { }

    public readonly Vector2 GetMaxScrollOffset(Vector2 viewportSize) => new(
        EnableHorizontal ? Math.Max(0, ContentSize.X - viewportSize.X) : 0,
        EnableVertical ? Math.Max(0, ContentSize.Y - viewportSize.Y) : 0);

    public readonly Vector2 ClampScrollOffset(Vector2 offset, Vector2 viewportSize) =>
        Vector2.Clamp(offset, Vector2.Zero, GetMaxScrollOffset(viewportSize));
}

public partial record struct UIScrollbar(
    [Sia] bool IsHorizontal,
    [Sia] float ThumbSize,
    [Sia] float ThumbPosition,
    [Sia] bool IsVisible,
    [Sia] bool IsDragging)
{
    public UIScrollbar() : this(false, 0.1f, 0f, true, false) { }
}