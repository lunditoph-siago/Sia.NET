using System.Drawing;
using System.Numerics;

namespace Sia.Examples.Runtime.Components;

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

public readonly record struct UIClickEvent(Entity Target, Vector2 Position, MouseButton Button) : IUIEvent;
public readonly record struct UIHoverEnterEvent(Entity Target, Vector2 Position) : IUIEvent;
public readonly record struct UIHoverExitEvent(Entity Target, Vector2 Position) : IUIEvent;

public enum MouseButton
{
    Left = 0,
    Right = 1,
    Middle = 2
}

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
    [Sia] bool IsEnabled)
{
    public UIEventListener() : this(true, true, true, true) { }
}