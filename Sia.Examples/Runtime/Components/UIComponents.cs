using System.Drawing;
using System.Numerics;
using System.Runtime.CompilerServices;
using Silk.NET.Input;
using Sia.Reactors;

namespace Sia.Examples.Runtime.Components;

[SiaEvents]
public partial class UIEvents
{
    // Basic interaction events
    public readonly record struct ElementClicked(Entity Target, Vector2 Position, MouseButton Button) : IEvent;
    public readonly record struct ElementDoubleClicked(Entity Target, Vector2 Position, MouseButton Button) : IEvent;
    public readonly record struct ElementHoverEnter(Entity Target, Vector2 Position) : IEvent;
    public readonly record struct ElementHoverExit(Entity Target, Vector2 Position) : IEvent;
    public readonly record struct ElementFocused(Entity Target) : IEvent;
    public readonly record struct ElementUnfocused(Entity Target) : IEvent;

    // Button specific events
    public readonly record struct ButtonPressed(Entity Button) : IEvent;
    public readonly record struct ButtonReleased(Entity Button) : IEvent;

    // Scroll events
    public readonly record struct ScrollPerformed(Entity Target, Vector2 Delta, Vector2 Position) : IEvent;

    // State change events
    public readonly record struct VisibilityChanged(Entity Target, bool IsVisible) : IEvent;
    public readonly record struct InteractabilityChanged(Entity Target, bool IsInteractable) : IEvent;
}

public enum TextAlignment : byte
{
    Left,
    Center,
    Right
}

[Flags]
public enum UIStateFlags : byte
{
    None = 0,
    Focused = 1 << 0,
    Hovered = 1 << 1,
    Pressed = 1 << 2,
    Disabled = 1 << 3
}

[Flags]
public enum ScrollDirection : byte
{
    None = 0,
    Horizontal = 1 << 0,
    Vertical = 1 << 1,
    Both = Horizontal | Vertical
}

[Flags]
public enum UIEventMask : byte
{
    None = 0,
    Click = 1 << 0,
    Hover = 1 << 1,
    Focus = 1 << 2,
    Scroll = 1 << 3,
    Keyboard = 1 << 4,
    All = Click | Hover | Focus | Scroll | Keyboard
}

public sealed class UIHierarchyTag;

public partial record struct UIElement(
    [Sia] Vector2 Position,
    [Sia] Vector2 Size,
    [Sia] bool IsVisible,
    [Sia] bool IsInteractable)
{
    public UIElement() : this(Vector2.Zero, new Vector2(100, 30), true, true) { }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool Contains(Vector2 point) =>
        point.X >= Position.X && point.X <= Position.X + Size.X &&
        point.Y >= Position.Y && point.Y <= Position.Y + Size.Y;

    public readonly Vector2 Center => Position + Size * 0.5f;
    public readonly RectangleF Bounds => new(Position.X, Position.Y, Size.X, Size.Y);

    public readonly record struct SetVisible(bool IsVisible) : ICommand
    {
        public void Execute(World world, Entity target)
        {
            if (!target.IsValid || !target.Contains<UIElement>()) return;

            ref readonly var element = ref target.Get<UIElement>();
            if (element.IsVisible == IsVisible) return;

            new View(target).IsVisible = IsVisible;
            world.Send(target, new UIEvents.VisibilityChanged(target, IsVisible));
        }
    }

    public readonly record struct SetInteractable(bool IsInteractable) : ICommand
    {
        public void Execute(World world, Entity target)
        {
            if (!target.IsValid || !target.Contains<UIElement>()) return;

            ref readonly var element = ref target.Get<UIElement>();
            if (element.IsInteractable == IsInteractable) return;

            new View(target).IsInteractable = IsInteractable;
            world.Send(target, new UIEvents.InteractabilityChanged(target, IsInteractable));
        }
    }

    public readonly record struct AddChild(Entity Child) : ICommand
    {
        public void Execute(World world, Entity target)
        {
            if (!IsValidHierarchyOperation(target, Child)) return;

            Child.Execute(new Node<UIHierarchyTag>.SetParent(target));

            if (target.Contains<UILayer>() && Child.Contains<UILayer>())
            {
                var parentLayer = target.Get<UILayer>().Value;
                new UILayer.View(Child).Value = parentLayer + 1;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsValidHierarchyOperation(Entity target, Entity child) =>
            target.IsValid && child.IsValid &&
            target.Contains<Node<UIHierarchyTag>>() &&
            child.Contains<Node<UIHierarchyTag>>();
    }
}

public partial record struct UILayer(
    [Sia] int Value)
{
    public UILayer() : this(0) { }
}

public partial record struct UIState(
    [Sia] UIStateFlags Flags)
{
    public UIState() : this(UIStateFlags.None) { }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool HasFlag(UIStateFlags flag) => (Flags & flag) == flag;
}

public partial record struct UIText(
    [Sia] string Content,
    [Sia] Color Color,
    [Sia] float FontSize,
    [Sia] TextAlignment Alignment)
{
    public UIText() : this(string.Empty, Color.Black, 14f, TextAlignment.Left) { }

    public readonly bool IsEmpty => string.IsNullOrWhiteSpace(Content);

    public readonly record struct SetText(string Content) : ICommand
    {
        public void Execute(World world, Entity target)
        {
            new View(target).Content = Content ?? string.Empty;

            if (target.Contains<UIScrollable>())
            {
                new UIScrollable.View(target).ScrollOffset = Vector2.Zero;
            }
        }
    }
}

public partial record struct UIStyle(
    [Sia] Color BackgroundColor,
    [Sia] Color BorderColor,
    [Sia] float BorderWidth,
    [Sia] float CornerRadius)
{
    public UIStyle() : this(Color.Transparent, Color.Transparent, 0f, 0f) { }

    public readonly bool HasBorder => BorderWidth > 0 && BorderColor.A > 0;
    public readonly bool HasBackground => BackgroundColor.A > 0;
    public readonly bool HasRoundedCorners => CornerRadius > 0;

    public static readonly UIStyle Button = new(Color.LightGray, Color.Gray, 1f, 4f);
    public static readonly UIStyle Panel = new(Color.FromArgb(80, 40, 40, 50), Color.Transparent, 0f, 2f);
    public static readonly UIStyle Input = new(Color.White, Color.Gray, 1f, 2f);
}

public partial record struct UIButton(
    [Sia] UIStyle NormalStyle,
    [Sia] UIStyle HoverStyle,
    [Sia] UIStyle PressedStyle)
{
    public UIButton() : this(
        NormalStyle: UIStyle.Button,
        HoverStyle: UIStyle.Button with { BackgroundColor = Color.FromArgb(255, 200, 200, 200) },
        PressedStyle: UIStyle.Button with { BackgroundColor = Color.FromArgb(255, 150, 150, 150) })
    { }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly UIStyle GetStyleForState(UIStateFlags state) => state switch
    {
        _ when (state & UIStateFlags.Pressed) != 0 => PressedStyle,
        _ when (state & UIStateFlags.Hovered) != 0 => HoverStyle,
        _ => NormalStyle
    };
}

public partial record struct UIScrollable(
    [Sia] Vector2 ContentSize,
    [Sia] Vector2 ScrollOffset,
    [Sia] ScrollDirection Direction,
    [Sia] float ScrollSpeed)
{
    public UIScrollable() : this(Vector2.Zero, Vector2.Zero, ScrollDirection.Vertical, 20f) { }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly Vector2 GetMaxScrollOffset(Vector2 viewportSize) =>
        Vector2.Max(Vector2.Zero, ContentSize - viewportSize);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly Vector2 ClampScrollOffset(Vector2 offset, Vector2 viewportSize) =>
        Vector2.Clamp(offset, Vector2.Zero, GetMaxScrollOffset(viewportSize));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool CanScroll(ScrollDirection direction) => (Direction & direction) == direction;

    public readonly record struct ScrollTo(Vector2 Offset) : ICommand
    {
        public void Execute(World world, Entity target)
        {
            ref readonly var scrollable = ref target.Get<UIScrollable>();
            ref readonly var element = ref target.Get<UIElement>();
            var clampedOffset = scrollable.ClampScrollOffset(Offset, element.Size);

            new View(target).ScrollOffset = clampedOffset;
        }
    }
}

public partial record struct UIEventListener(
    [Sia] bool IsEnabled,
    [Sia] UIEventMask EventMask)
{
    public UIEventListener() : this(true, UIEventMask.All) { }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool AcceptsEvent(UIEventMask eventType) => (EventMask & eventType) == eventType;
}