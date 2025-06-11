using System.Numerics;
using Sia.Examples.Runtime.Components;
using Silk.NET.Input;

namespace Sia.Examples.Runtime.Systems;

public class KeyboardStateSystem : EventSystemBase
{
    public override void Initialize(World world)
    {
        base.Initialize(world);
        RecordEvents<InputEvents>();
    }

    protected override void HandleEvent<TEvent>(Entity entity, in TEvent @event)
    {
        switch (@event)
        {
            case InputEvents.KeyPressed keyPressed:
                UpdateKeyboardState(entity, keyPressed.Key, true);
                break;
            case InputEvents.KeyReleased keyReleased:
                UpdateKeyboardState(entity, keyReleased.Key, false);
                break;
        }
    }

    private static void UpdateKeyboardState(Entity entity, Key key, bool isPressed)
    {
        if (!entity.Contains<KeyboardState>()) return;

        var currentKeys = entity.Get<KeyboardState>().PressedKeys;
        var newKeys = isPressed
            ? currentKeys.Add(key)
            : currentKeys.Remove(key);

        if (newKeys != currentKeys)
        {
            new KeyboardState.View(entity).PressedKeys = newKeys;
        }
    }
}

public class MouseStateSystem : EventSystemBase
{
    public override void Initialize(World world)
    {
        base.Initialize(world);
        RecordEvents<InputEvents>();
    }

    protected override void HandleEvent<TEvent>(Entity entity, in TEvent @event)
    {
        switch (@event)
        {
            case InputEvents.MouseMoved mouseMoved:
                UpdateMousePosition(entity, mouseMoved.Position, mouseMoved.Delta);
                break;
        }
    }

    private static void UpdateMousePosition(Entity entity, Vector2 position, Vector2 delta)
    {
        if (!entity.Contains<MouseState>()) return;

        var view = new MouseState.View(entity);
        view.Position = position;
        view.DeltaPosition = delta;
    }
}

public class MouseButtonStateSystem : EventSystemBase
{
    public override void Initialize(World world)
    {
        base.Initialize(world);
        RecordEvents<InputEvents>();
    }

    protected override void HandleEvent<TEvent>(Entity entity, in TEvent @event)
    {
        switch (@event)
        {
            case InputEvents.MouseButtonPressed mousePressed:
                UpdateMouseButtonState(entity, mousePressed.Button, true);
                break;
            case InputEvents.MouseButtonReleased mouseReleased:
                UpdateMouseButtonState(entity, mouseReleased.Button, false);
                break;
        }
    }

    private static void UpdateMouseButtonState(Entity entity, MouseButton button, bool isPressed)
    {
        if (!entity.Contains<MouseButtonState>()) return;

        var currentButtons = entity.Get<MouseButtonState>().PressedButtons;
        var newButtons = isPressed
            ? currentButtons.Add(button)
            : currentButtons.Remove(button);

        if (newButtons != currentButtons)
        {
            new MouseButtonState.View(entity).PressedButtons = newButtons;
        }
    }
}

public class MouseScrollStateSystem : EventSystemBase
{
    public override void Initialize(World world)
    {
        base.Initialize(world);
        RecordEvents<InputEvents>();
    }

    protected override void HandleEvent<TEvent>(Entity entity, in TEvent @event)
    {
        switch (@event)
        {
            case InputEvents.MouseScrolled mouseScrolled:
                UpdateScrollState(entity, mouseScrolled.Delta);
                break;
        }
    }

    private static void UpdateScrollState(Entity entity, Vector2 scrollDelta)
    {
        if (!entity.Contains<ScrollState>()) return;

        new ScrollState.View(entity).ScrollDelta = scrollDelta;
    }
}