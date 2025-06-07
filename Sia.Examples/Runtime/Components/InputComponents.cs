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
    [Sia] bool IsEnabled,
    [Sia] bool CanReceiveKeyboard,
    [Sia] bool CanReceiveMouse)
{
    public InputReceiver() : this(true, false, true) { }
}

public partial record struct Clickable(
    [Sia] Vector2 Size,
    [Sia] bool IsHovered,
    [Sia] bool IsPressed)
{
    public Clickable() : this(Vector2.One * 100, false, false) { }

    public readonly record struct Click() : IEvent;

    public readonly record struct Hover() : IEvent;

    public readonly record struct Leave() : IEvent;
}