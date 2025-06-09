using System.Numerics;
using System.Runtime.CompilerServices;
using Sia.Examples.Runtime.Components;
using Sia.Reactors;

namespace Sia.Examples.Runtime.Systems;

public class UIEventSystem : EventSystemBase
{
    private Vector2 _lastMousePosition;
    private readonly Dictionary<Entity, bool> _hoverStates = new();
    private readonly List<Entity> _sortedUIElements = new();
    private bool _uiListDirty = true;

    private readonly ref struct UIBounds(Vector2 position, Vector2 size)
    {
        public readonly Vector2 Position = position;
        public readonly Vector2 Size = size;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(Vector2 point) =>
            point.X >= Position.X &&
            point.X <= Position.X + Size.X &&
            point.Y >= Position.Y &&
            point.Y <= Position.Y + Size.Y;
    }

    public override void Initialize(World world)
    {
        base.Initialize(world);

        RecordEvent<InputEvents.MouseClick>();
        RecordEvent<InputEvents.MouseMove>();
        RecordEvent<InputEvents.MouseScroll>();

        RecordEvent<UIElement.SetPosition>();
        RecordEvent<UIElement.SetSize>();
        RecordEvent<UIElement.SetLayer>();
    }

    protected override void HandleEvent<TEvent>(Entity entity, in TEvent @event)
    {
        switch (@event)
        {
            case InputEvents.MouseClick mouseClick:
                HandleMouseClick(mouseClick);
                break;
            case InputEvents.MouseMove mouseMove:
                HandleMouseMove(mouseMove);
                break;
            case InputEvents.MouseScroll mouseScroll:
                HandleMouseScroll(mouseScroll);
                break;
            case UIElement.SetPosition:
            case UIElement.SetSize:
            case UIElement.SetLayer:
                _uiListDirty = true;
                break;
        }
    }

    private void RefreshUIList()
    {
        if (!_uiListDirty) return;

        _sortedUIElements.Clear();

        var uiQuery = World.Query(Matchers.Of<UIElement, UIEventListener>());
        foreach (var entity in uiQuery)
        {
            ref readonly var uiElement = ref entity.Get<UIElement>();
            if (uiElement is { IsVisible: true, IsInteractable: true })
            {
                _sortedUIElements.Add(entity);
            }
        }

        // Sort by layer (higher layer first)
        _sortedUIElements.Sort(static (a, b) =>
        {
            var layerA = a.Get<UIElement>().Layer;
            var layerB = b.Get<UIElement>().Layer;
            return layerB.CompareTo(layerA);
        });

        _uiListDirty = false;
    }

    private void HandleMouseClick(InputEvents.MouseClick mouseClick)
    {
        RefreshUIList();

        var mousePos = mouseClick.Position;
        var button = mouseClick.Button;

        foreach (var entity in _sortedUIElements)
        {
            ref readonly var uiElement = ref entity.Get<UIElement>();
            ref readonly var eventListener = ref entity.Get<UIEventListener>();

            if (!eventListener.IsEnabled || !eventListener.ListenToClick)
                continue;

            var bounds = new UIBounds(uiElement.Position, uiElement.Size);
            if (bounds.Contains(mousePos))
            {
                // Update interaction state
                if (entity.Contains<UIInteractionState>())
                {
                    var stateView = new UIInteractionState.View(entity);
                    stateView.IsPressed = true;
                    stateView.PressedButton = button;
                }

                HandleButtonClick(entity, mousePos, button);

                // Send UI event
                World.Send(entity, new UIClickEvent(entity, mousePos, button));
                break; // Only process the topmost element
            }
        }
    }

    private void HandleMouseMove(InputEvents.MouseMove mouseMove)
    {
        RefreshUIList();

        var mousePos = mouseMove.Position;
        _lastMousePosition = mousePos;

        Entity? currentHoveredEntity = null;

        // Find the topmost hovered element
        foreach (var entity in _sortedUIElements)
        {
            ref readonly var uiElement = ref entity.Get<UIElement>();
            ref readonly var eventListener = ref entity.Get<UIEventListener>();

            if (!eventListener.IsEnabled || !eventListener.ListenToHover)
                continue;

            var bounds = new UIBounds(uiElement.Position, uiElement.Size);
            if (bounds.Contains(mousePos))
            {
                currentHoveredEntity = entity;
                break; // Only process the topmost element
            }
        }

        // Handle hover state changes
        var entitiesToRemove = new List<Entity>();
        foreach (var kvp in _hoverStates)
        {
            if (kvp.Value && kvp.Key != currentHoveredEntity)
            {
                var entity = kvp.Key;
                entitiesToRemove.Add(entity);

                if (entity.Contains<UIInteractionState>())
                {
                    var stateView = new UIInteractionState.View(entity);
                    stateView.IsHovered = false;
                    stateView.IsPressed = false;
                }

                HandleButtonHoverExit(entity, mousePos);
                World.Send(entity, new UIHoverExitEvent(entity, mousePos));
            }
        }

        // Clean up entities no longer being hovered
        foreach (var entity in entitiesToRemove)
        {
            _hoverStates[entity] = false;
        }

        // Handle new hover state
        if (currentHoveredEntity != null)
        {
            if (!_hoverStates.GetValueOrDefault(currentHoveredEntity, false))
            {
                _hoverStates[currentHoveredEntity] = true;

                if (currentHoveredEntity.Contains<UIInteractionState>())
                {
                    var stateView = new UIInteractionState.View(currentHoveredEntity);
                    stateView.IsHovered = true;
                }

                HandleButtonHoverEnter(currentHoveredEntity, mousePos);
                World.Send(currentHoveredEntity, new UIHoverEnterEvent(currentHoveredEntity, mousePos));
            }
        }
    }

    private void HandleMouseScroll(InputEvents.MouseScroll mouseScroll)
    {
        RefreshUIList();

        var mousePos = _lastMousePosition;
        var scrollDelta = mouseScroll.Delta;

        // Find the topmost scrollable element under the mouse position
        foreach (var entity in _sortedUIElements)
        {
            ref readonly var uiElement = ref entity.Get<UIElement>();

            // Check if it contains a scrollable component
            if (!entity.Contains<UIScrollable>()) continue;

            ref readonly var eventListener = ref entity.Get<UIEventListener>();
            if (!eventListener.IsEnabled || !eventListener.ListenToScroll) continue;

            var bounds = new UIBounds(uiElement.Position, uiElement.Size);
            if (bounds.Contains(mousePos))
            {
                ref var scrollable = ref entity.Get<UIScrollable>();
                if (!scrollable.IsEnabled) continue;

                // Calculate the new scroll offset
                var newScrollOffset = scrollable.ScrollOffset;

                if (scrollable.EnableVertical)
                    newScrollOffset.Y -= scrollDelta.Y * scrollable.ScrollSpeed.Y;

                if (scrollable.EnableHorizontal)
                    newScrollOffset.X -= scrollDelta.X * scrollable.ScrollSpeed.X;

                // Limit the scroll range
                newScrollOffset = scrollable.ClampScrollOffset(newScrollOffset, uiElement.Size);

                // Update the scroll offset
                scrollable = scrollable with { ScrollOffset = newScrollOffset };

                // Update the scrollbar positions (if any)
                UpdateScrollbarPositions(entity, scrollable, uiElement.Size);

                // Send the scroll event
                World.Send(entity, new UIScrollEvent(entity, mousePos, scrollDelta));
                break; // Only process the topmost scrollable element
            }
        }
    }

    private void UpdateScrollbarPositions(Entity entity, UIScrollable scrollable, Vector2 viewportSize)
    {
        if (!entity.Contains<Node<UIHierarchyTag>>()) return;

        var maxOffset = scrollable.GetMaxScrollOffset(viewportSize);
        foreach (var child in entity.Get<Node<UIHierarchyTag>>().Children)
        {
            if (!child.Contains<UIScrollbar>()) continue;

            ref readonly var scrollbar = ref child.Get<UIScrollbar>();
            var scrollbarView = new UIScrollbar.View(child);
            var isVertical = !scrollbar.IsHorizontal;
            var axis = isVertical ? maxOffset.Y : maxOffset.X;
            var viewportAxis = isVertical ? viewportSize.Y : viewportSize.X;
            var contentAxis = isVertical ? scrollable.ContentSize.Y : scrollable.ContentSize.X;
            var scrollAxis = isVertical ? scrollable.ScrollOffset.Y : scrollable.ScrollOffset.X;

            scrollbarView.IsVisible = axis > 0;
            if (axis > 0)
            {
                scrollbarView.ThumbPosition = scrollAxis / axis;
                scrollbarView.ThumbSize = Math.Clamp(viewportAxis / contentAxis, 0.1f, 1f);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandleButtonClick(Entity entity, Vector2 position, MouseButton button)
    {
        if (!IsButton(entity)) return;
        Console.WriteLine($"Button clicked at {position}");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandleButtonHoverEnter(Entity entity, Vector2 position)
    {
        if (!IsButton(entity)) return;

        ref readonly var button = ref entity.Get<UIButton>();
        if (!button.IsEnabled) return;

        if (entity.Contains<UIPanel>())
        {
            var panelView = new UIPanel.View(entity);
            panelView.BackgroundColor = button.HoverColor;
        }

        Console.WriteLine($"Button hover enter at {position}");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HandleButtonHoverExit(Entity entity, Vector2 position)
    {
        if (!IsButton(entity)) return;

        ref readonly var button = ref entity.Get<UIButton>();
        if (!button.IsEnabled) return;

        if (entity.Contains<UIPanel>())
        {
            var panelView = new UIPanel.View(entity);
            panelView.BackgroundColor = button.NormalColor;
        }

        Console.WriteLine($"Button hover exit at {position}");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsButton(Entity entity) =>
        entity.Contains<UIButton>() &&
        entity.Contains<UIElement>() &&
        entity.Contains<UIEventListener>();

    public override void Uninitialize(World world)
    {
        _hoverStates.Clear();
        _sortedUIElements.Clear();
        base.Uninitialize(world);
    }
}