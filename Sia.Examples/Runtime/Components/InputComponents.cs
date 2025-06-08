using System.Numerics;
using Silk.NET.Input;

namespace Sia.Examples.Runtime.Components;

[SiaEvents]
public partial class InputEvents
{
    public readonly record struct KeyDown(Key Key) : IEvent;

    public readonly record struct KeyUp(Key Key) : IEvent;

    public readonly record struct MouseClick(MouseButton Button, Vector2 Position) : IEvent;

    public readonly record struct MouseMove(Vector2 Position) : IEvent;

    public readonly record struct MouseScroll(Vector2 Delta) : IEvent;
}

public partial record struct InputReceiver(
    [Sia] bool IsEnabled)
{
    public InputReceiver() : this(true) { }
}

public partial record struct KeyboardReceiver(
    [Sia] bool IsEnabled,
    [Sia] bool ReceiveKeyDown,
    [Sia] bool ReceiveKeyUp)
{
    public KeyboardReceiver() : this(true, true, true) { }
}

public partial record struct MouseReceiver(
    [Sia] bool IsEnabled,
    [Sia] bool ReceiveClick,
    [Sia] bool ReceiveMove,
    [Sia] bool ReceiveScroll)
{
    public MouseReceiver() : this(true, true, true, true) { }
}