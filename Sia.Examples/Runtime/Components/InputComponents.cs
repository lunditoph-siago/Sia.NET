using System.Collections.Immutable;
using System.Numerics;
using Silk.NET.Input;

namespace Sia.Examples.Runtime.Components;

[SiaEvents]
public partial class InputEvents
{
    // Basic interaction events
    public readonly record struct KeyPressed(Key Key) : IEvent;
    public readonly record struct KeyReleased(Key Key) : IEvent;
    public readonly record struct MouseButtonPressed(MouseButton Button, Vector2 Position) : IEvent;
    public readonly record struct MouseButtonReleased(MouseButton Button, Vector2 Position) : IEvent;
    public readonly record struct MouseMoved(Vector2 Position, Vector2 Delta) : IEvent;
    public readonly record struct MouseScrolled(Vector2 Delta, Vector2 Position) : IEvent;

    // High level interaction events
    public readonly record struct Click(Vector2 Position, MouseButton Button) : IEvent;
    public readonly record struct DoubleClick(Vector2 Position, MouseButton Button) : IEvent;
}

public partial record struct InputDevice([Sia] string DeviceId, [Sia] bool IsConnected)
{
    public InputDevice() : this("default", true) { }
}

public partial record struct MouseState([Sia] Vector2 Position, [Sia] Vector2 DeltaPosition)
{
    public MouseState() : this(Vector2.Zero, Vector2.Zero) { }
}

public partial record struct KeyboardState([Sia] ImmutableHashSet<Key> PressedKeys)
{
    public KeyboardState() : this([]) { }
}

public partial record struct MouseButtonState([Sia] ImmutableHashSet<MouseButton> PressedButtons)
{
    public MouseButtonState() : this([]) { }
}

public partial record struct ScrollState([Sia] Vector2 ScrollDelta)
{
    public ScrollState() : this(Vector2.Zero) { }
}