using System.Drawing;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Sia.Examples.Runtime.Components;

[SiaEvents]
public partial class UILayoutEvents
{
    public readonly record struct LayoutInvalidated(Entity Target) : IEvent;
    public readonly record struct LayoutComputed(Entity Target, Vector2 ComputedSize) : IEvent;
}

public enum LayoutType : byte
{
    None,
    Flex,
    Absolute
}

public enum FlexDirection : byte
{
    Row,
    Column
}

public enum Alignment : byte
{
    Start,
    Center,
    End,
    Stretch
}

public enum SizeUnit : byte
{
    Pixel,
    Percent,
    Auto
}

public readonly record struct SizeValue(float Value, SizeUnit Unit)
{
    public static readonly SizeValue Auto = new(0, SizeUnit.Auto);
    public static readonly SizeValue Zero = new(0, SizeUnit.Pixel);

    public static SizeValue Pixels(float value) => new(value, SizeUnit.Pixel);
    public static SizeValue Percent(float value) => new(value, SizeUnit.Percent);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly float Resolve(float containerSize) => Unit switch
    {
        SizeUnit.Pixel => Value,
        SizeUnit.Percent => containerSize * (Value / 100f),
        SizeUnit.Auto => 0,
        _ => 0
    };
}

public readonly record struct LayoutConstraints(Vector2 AvailableSize)
{
    public static readonly LayoutConstraints Unconstrained = new(new Vector2(float.MaxValue));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly LayoutConstraints WithAvailableSize(Vector2 availableSize) =>
        new(availableSize);
}

public partial record struct UILayout(
    [Sia] LayoutType Type,
    [Sia] SizeValue Width,
    [Sia] SizeValue Height,
    [Sia] Vector4 Margin,      // left, top, right, bottom
    [Sia] Vector4 Padding,     // left, top, right, bottom
    [Sia] bool NeedsLayout)
{
    public UILayout() : this(
        LayoutType.None,
        SizeValue.Auto, SizeValue.Auto,
        Vector4.Zero, Vector4.Zero,
        true)
    { }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly Vector2 GetContentSize(Vector2 elementSize)
    {
        return new Vector2(
            Math.Max(0, elementSize.X - Padding.X - Padding.Z), // left + right padding
            Math.Max(0, elementSize.Y - Padding.Y - Padding.W)  // top + bottom padding
        );
    }

    public readonly record struct InvalidateLayout() : ICommand
    {
        public void Execute(World world, Entity target)
        {
            if (!target.Contains<UILayout>()) return;

            new View(target).NeedsLayout = true;
            world.Send(target, new UILayoutEvents.LayoutInvalidated(target));
        }
    }
}

public partial record struct UIComputedLayout(
    [Sia] Vector2 Position,
    [Sia] Vector2 Size,
    [Sia] Vector2 ContentPosition,
    [Sia] Vector2 ContentSize)
{
    public UIComputedLayout() : this(
        Vector2.Zero, Vector2.Zero,
        Vector2.Zero, Vector2.Zero)
    { }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly RectangleF GetBounds() => new(Position.X, Position.Y, Size.X, Size.Y);
}

public partial record struct UIFlexContainer(
    [Sia] FlexDirection Direction,
    [Sia] Alignment JustifyContent,
    [Sia] Alignment AlignItems,
    [Sia] float Gap)
{
    public UIFlexContainer() : this(
        FlexDirection.Row,
        Alignment.Start,
        Alignment.Stretch,
        0f)
    { }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool IsRowDirection() => Direction == FlexDirection.Row;
}

public partial record struct UIFlexItem(
    [Sia] float Grow,
    [Sia] float Shrink,
    [Sia] SizeValue Basis)
{
    public UIFlexItem() : this(0f, 1f, SizeValue.Auto) { }
}

public partial record struct UIAbsolutePosition(
    [Sia] SizeValue Left,
    [Sia] SizeValue Top,
    [Sia] SizeValue Right,
    [Sia] SizeValue Bottom)
{
    public UIAbsolutePosition() : this(
        SizeValue.Auto, SizeValue.Auto,
        SizeValue.Auto, SizeValue.Auto)
    { }
}