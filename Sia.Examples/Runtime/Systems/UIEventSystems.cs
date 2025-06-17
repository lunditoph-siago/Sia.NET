using System.Numerics;
using System.Runtime.CompilerServices;
using Sia.Examples.Runtime.Components;
using Silk.NET.Input;

namespace Sia.Examples.Runtime.Systems;

public sealed class UIClickHitTestSystem : EventSystemBase
{
    private readonly List<Entity> _candidateElements = new(32);

    public override void Initialize(World world)
    {
        base.Initialize(world);
        RecordEvents<InputEvents>();
    }

    protected override void HandleEvent<TEvent>(Entity entity, in TEvent @event)
    {
        switch (@event)
        {
            case InputEvents.Click { Position: var position, Button: var button }:
                HandleClick(position, button);
                break;
            case InputEvents.DoubleClick { Position: var position, Button: var button }:
                HandleDoubleClick(position, button);
                break;
        }
    }

    private void HandleClick(Vector2 position, MouseButton button)
    {
        if (FindTopUIElementAt(position) is { } hitElement)
        {
            World.Send(hitElement, new UIEvents.ElementClicked(hitElement, position, button));
        }
    }

    private void HandleDoubleClick(Vector2 position, MouseButton button)
    {
        if (FindTopUIElementAt(position) is { } hitElement)
        {
            World.Send(hitElement, new UIEvents.ElementDoubleClicked(hitElement, position, button));
        }
    }

    private Entity? FindTopUIElementAt(Vector2 position)
    {
        _candidateElements.Clear();

        var query = World.Query(Matchers.Of<UIElement, UIEventListener>());

        foreach (var entity in query)
        {
            ref readonly var element = ref entity.Get<UIElement>();
            ref readonly var listener = ref entity.Get<UIEventListener>();

            if (!element.IsVisible || !element.IsInteractable || !listener.IsEnabled)
                continue;

            if (!listener.AcceptsEvent(UIEventMask.Click))
                continue;

            if (!element.Contains(position))
                continue;

            _candidateElements.Add(entity);
        }

        return FindTopElementByLayer(_candidateElements);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Entity? FindTopElementByLayer(IReadOnlyList<Entity> candidates)
    {
        return candidates.Count switch
        {
            0 => null,
            1 => candidates[0],
            _ => FindTopElement(candidates)
        };

        static Entity FindTopElement(IReadOnlyList<Entity> candidates)
        {
            var topElement = candidates[0];
            var topLayer = topElement.Contains<UILayer>() ? topElement.Get<UILayer>().Value : 0;

            for (var i = 1; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                var layer = candidate.Contains<UILayer>() ? candidate.Get<UILayer>().Value : 0;
                if (layer > topLayer)
                {
                    topElement = candidate;
                    topLayer = layer;
                }
            }

            return topElement;
        }
    }
}

public sealed class UIHoverStateSystem : EventSystemBase
{
    private Entity? _currentHoveredElement;

    public override void Initialize(World world)
    {
        base.Initialize(world);
        RecordEvents<InputEvents>();
    }

    protected override void HandleEvent<TEvent>(Entity entity, in TEvent @event)
    {
        if (@event is InputEvents.MouseMoved { Position: var position })
            HandleHover(position);
    }

    private void HandleHover(Vector2 position)
    {
        var hitElement = FindTopUIElementAt(position);

        if (hitElement != _currentHoveredElement)
        {
            if (_currentHoveredElement is { IsValid: true })
            {
                ClearHoverState(_currentHoveredElement);
                World.Send(_currentHoveredElement, new UIEvents.ElementHoverExit(_currentHoveredElement, position));
            }

            if (hitElement is { IsValid: true })
            {
                SetHoverState(hitElement);
                World.Send(hitElement, new UIEvents.ElementHoverEnter(hitElement, position));
            }

            _currentHoveredElement = hitElement;
        }
    }

    private Entity? FindTopUIElementAt(Vector2 position)
    {
        var query = World.Query(Matchers.Of<UIElement, UIEventListener>());
        Entity? topElement = null;
        var topLayer = int.MinValue;

        foreach (var entity in query)
        {
            ref readonly var element = ref entity.Get<UIElement>();
            ref readonly var listener = ref entity.Get<UIEventListener>();

            if (!element.IsVisible || !element.IsInteractable || !listener.IsEnabled)
                continue;

            if (!listener.AcceptsEvent(UIEventMask.Hover))
                continue;

            if (!element.Contains(position))
                continue;

            var layer = entity.Contains<UILayer>() ? entity.Get<UILayer>().Value : 0;
            if (layer >= topLayer)
            {
                topElement = entity;
                topLayer = layer;
            }
        }

        return topElement;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void SetHoverState(Entity element)
    {
        if (element.Contains<UIState>())
        {
            var state = element.Get<UIState>();
            new UIState.View(element).Flags = state.Flags | UIStateFlags.Hovered;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ClearHoverState(Entity element)
    {
        if (element.Contains<UIState>())
        {
            var state = element.Get<UIState>();
            new UIState.View(element).Flags = state.Flags & ~UIStateFlags.Hovered;
        }
    }
}

public sealed class UIButtonInteractionSystem : EventSystemBase
{
    private readonly HashSet<Entity> _pressedButtons = [];

    public override void Initialize(World world)
    {
        base.Initialize(world);
        RecordEvents<UIEvents>();
        RecordEvents<InputEvents>();
    }

    protected override void HandleEvent<TEvent>(Entity entity, in TEvent @event)
    {
        switch (@event)
        {
            case UIEvents.ElementClicked { Target: var target, Position: var position, Button: var button }
                when target.Contains<UIButton>():
                HandleButtonClick(target, position, button);
                break;
            case InputEvents.MouseButtonPressed { Position: var position, Button: var button }:
                HandleGlobalMousePressed(position, button);
                break;
            case InputEvents.MouseButtonReleased:
                HandleGlobalMouseReleased();
                break;
        }
    }

    private void HandleButtonClick(Entity button, Vector2 position, MouseButton mouseButton)
    {
        if (mouseButton == MouseButton.Left)
        {
            World.Send(button, new UIEvents.ButtonPressed(button));
        }
    }

    private void HandleGlobalMousePressed(Vector2 position, MouseButton button)
    {
        if (button != MouseButton.Left) return;

        if (FindButtonAt(position) is { } hitButton)
        {
            SetPressedState(hitButton, true);
            _pressedButtons.Add(hitButton);
        }
    }

    private void HandleGlobalMouseReleased()
    {
        var allPressedButtons = new HashSet<Entity>(_pressedButtons);

        var buttonQuery = World.Query(Matchers.Of<UIButton, UIState>());
        foreach (var entity in buttonQuery)
        {
            if (entity.Get<UIState>().HasFlag(UIStateFlags.Pressed))
            {
                allPressedButtons.Add(entity);
            }
        }

        _pressedButtons.Clear();

        foreach (var button in allPressedButtons)
        {
            if (button is { IsValid: true } && button.Contains<UIState>())
            {
                SetPressedState(button, false);
                World.Send(button, new UIEvents.ButtonReleased(button));
            }
        }
    }

    private Entity? FindButtonAt(Vector2 position)
    {
        var query = World.Query(Matchers.Of<UIElement, UIButton, UIEventListener>());
        Entity? topButton = null;
        var topLayer = int.MinValue;

        foreach (var entity in query)
        {
            ref readonly var element = ref entity.Get<UIElement>();
            ref readonly var listener = ref entity.Get<UIEventListener>();

            if (!element.IsVisible || !element.IsInteractable || !listener.IsEnabled)
                continue;

            if (!element.Contains(position))
                continue;

            var layer = entity.Contains<UILayer>() ? entity.Get<UILayer>().Value : 0;
            if (layer >= topLayer)
            {
                topButton = entity;
                topLayer = layer;
            }
        }

        return topButton;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void SetPressedState(Entity button, bool isPressed)
    {
        if (!button.Contains<UIState>()) return;

        var state = button.Get<UIState>();
        var newFlags = isPressed
            ? state.Flags | UIStateFlags.Pressed
            : state.Flags & ~UIStateFlags.Pressed;

        new UIState.View(button).Flags = newFlags;
    }
}

public sealed class UIScrollInteractionSystem : EventSystemBase
{
    public override void Initialize(World world)
    {
        base.Initialize(world);
        RecordEvents<InputEvents>();
    }

    protected override void HandleEvent<TEvent>(Entity entity, in TEvent @event)
    {
        if (@event is InputEvents.MouseScrolled { Position: var position, Delta: var scrollDelta })
            HandleScroll(position, scrollDelta);
    }

    private void HandleScroll(Vector2 position, Vector2 scrollDelta)
    {
        if (FindScrollableElementAt(position) is not { } scrollableElement) return;

        ref readonly var scrollable = ref scrollableElement.Get<UIScrollable>();
        ref readonly var element = ref scrollableElement.Get<UIElement>();

        var currentOffset = scrollable.ScrollOffset;
        var newOffset = CalculateNewScrollOffset(scrollable, scrollDelta);
        var clampedOffset = scrollable.ClampScrollOffset(newOffset, element.Size);

        if (clampedOffset != currentOffset)
        {
            new UIScrollable.View(scrollableElement).ScrollOffset = clampedOffset;
            World.Send(scrollableElement, new UIEvents.ScrollPerformed(scrollableElement, scrollDelta, position));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector2 CalculateNewScrollOffset(in UIScrollable scrollable, Vector2 scrollDelta)
    {
        var newOffset = scrollable.ScrollOffset;

        if (scrollable.CanScroll(ScrollDirection.Vertical))
            newOffset.Y -= scrollDelta.Y * scrollable.ScrollSpeed;

        if (scrollable.CanScroll(ScrollDirection.Horizontal))
            newOffset.X += scrollDelta.X * scrollable.ScrollSpeed;

        return newOffset;
    }

    private Entity? FindScrollableElementAt(Vector2 position)
    {
        var query = World.Query(Matchers.Of<UIElement, UIScrollable, UIEventListener>());
        Entity? topScrollable = null;
        var topLayer = int.MinValue;

        foreach (var entity in query)
        {
            ref readonly var element = ref entity.Get<UIElement>();
            ref readonly var listener = ref entity.Get<UIEventListener>();

            if (!element.IsVisible || !listener.IsEnabled)
                continue;

            if (!listener.AcceptsEvent(UIEventMask.Scroll))
                continue;

            if (!element.Contains(position))
                continue;

            var layer = entity.Contains<UILayer>() ? entity.Get<UILayer>().Value : 0;
            if (layer >= topLayer)
            {
                topScrollable = entity;
                topLayer = layer;
            }
        }

        return topScrollable;
    }
}