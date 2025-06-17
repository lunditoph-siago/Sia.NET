using System.Numerics;
using System.Runtime.CompilerServices;

namespace Sia.Examples.Runtime.Components;

public enum LayoutType : byte
{
    None = 0,
    Vertical = 1,      // Vertical Layout
    Horizontal = 2,    // Horizontal Layout
    Absolute = 3,      // Absolute Layout
    Static = 4         // Static Layout (children positioned at container origin)
}

public enum LayoutAlignment : byte
{
    Start = 0,     // Start Alignment (Left/Top)
    Center = 1,    // Center Alignment
    End = 2,       // End Alignment (Right/Bottom)
    Stretch = 3    // Stretch Alignment
}

public partial record struct UILayout(
    [Sia] LayoutType Type,
    [Sia] Vector2 Spacing,
    [Sia] LayoutAlignment Alignment,
    [Sia] bool AutoResize)
{
    public UILayout() : this(LayoutType.None, new Vector2(5f), LayoutAlignment.Start, true) { }
}

public partial record struct UILayoutConstraints(
    [Sia] Vector2 MinSize,
    [Sia] Vector2 MaxSize,
    [Sia] Vector2 PreferredSize,
    [Sia] bool ExpandHorizontal,
    [Sia] bool ExpandVertical)
{
    public UILayoutConstraints() : this(
        MinSize: Vector2.Zero,
        MaxSize: new Vector2(float.MaxValue),
        PreferredSize: new Vector2(100, 30),
        ExpandHorizontal: false,
        ExpandVertical: false)
    { }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly Vector2 ClampSize(Vector2 size) => Vector2.Clamp(size, MinSize, MaxSize);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly Vector2 GetActualSize(Vector2 availableSize, Vector2 contentSize)
    {
        var width = ExpandHorizontal ? availableSize.X :
                   Math.Max(MinSize.X, Math.Min(MaxSize.X, contentSize.X));

        var height = ExpandVertical ? availableSize.Y :
                    Math.Max(MinSize.Y, Math.Min(MaxSize.Y, contentSize.Y));

        return new Vector2(width, height);
    }
}

public readonly record struct UIMargin(float Left, float Top, float Right, float Bottom)
{
    public UIMargin(float all) : this(all, all, all, all) { }
    public UIMargin(float horizontal, float vertical) : this(horizontal, vertical, horizontal, vertical) { }

    public readonly Vector2 Size => new(Left + Right, Top + Bottom);
    public readonly Vector2 TopLeft => new(Left, Top);

    public static readonly UIMargin Zero = new(0);
}

public readonly record struct UIPadding(float Left, float Top, float Right, float Bottom)
{
    public UIPadding(float all) : this(all, all, all, all) { }
    public UIPadding(float horizontal, float vertical) : this(horizontal, vertical, horizontal, vertical) { }

    public readonly Vector2 Size => new(Left + Right, Top + Bottom);
    public readonly Vector2 TopLeft => new(Left, Top);

    public static readonly UIPadding Zero = new(0);
}