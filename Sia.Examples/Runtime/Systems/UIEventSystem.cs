using System.Numerics;
using System.Runtime.CompilerServices;
using Sia.Examples.Runtime.Components;

namespace Sia.Examples.Runtime.Systems;

public class UIEventSystem : EventSystemBase
{
    private Vector2 _lastMousePosition;
    private readonly Dictionary<Entity, bool> _hoverStates = new();

    private readonly List<Entity> _sortedUIElements = new();
    private bool _uiListDirty = true;

    public override void Initialize(World world)
    {
        base.Initialize(world);

        RecordEvent<InputEvents.MouseClick>();
        RecordEvent<InputEvents.MouseMove>();

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
            ref var uiElement = ref entity.Get<UIElement>();
            if (uiElement is { IsVisible: true, IsInteractable: true })
            {
                _sortedUIElements.Add(entity);
            }
        }

        // Sort by layer (higher layer first)
        _sortedUIElements.Sort((a, b) =>
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
            ref var uiElement = ref entity.Get<UIElement>();
            ref var eventListener = ref entity.Get<UIEventListener>();

            if (!eventListener.IsEnabled || !eventListener.ListenToClick)
                continue;

            // AABB collision detection
            if (IsPointInBounds(mousePos, uiElement.Position, uiElement.Size))
            {
                // Update interaction state
                if (entity.Contains<UIInteractionState>())
                {
                    var stateView = new UIInteractionState.View(entity);
                    stateView.IsPressed = true;
                    stateView.PressedButton = button;
                }

                // Handle button click logic
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
            ref var uiElement = ref entity.Get<UIElement>();
            ref var eventListener = ref entity.Get<UIEventListener>();

            if (!eventListener.IsEnabled || !eventListener.ListenToHover)
                continue;

            if (IsPointInBounds(mousePos, uiElement.Position, uiElement.Size))
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsPointInBounds(Vector2 point, Vector2 position, Vector2 size)
    {
        return point.X >= position.X &&
               point.X <= position.X + size.X &&
               point.Y >= position.Y &&
               point.Y <= position.Y + size.Y;
    }

    private void HandleButtonClick(Entity entity, Vector2 position, MouseButton button)
    {
        if (!IsButton(entity)) return;
        Console.WriteLine($"Button clicked, position: {position}");
    }

    private void HandleButtonHoverEnter(Entity entity, Vector2 position)
    {
        if (!IsButton(entity)) return;

        ref var button = ref entity.Get<UIButton>();
        if (!button.IsEnabled) return;

        if (entity.Contains<UIPanel>())
        {
            var panelView = new UIPanel.View(entity);
            panelView.BackgroundColor = button.HoverColor;
        }

        Console.WriteLine($"Button hover enter, position: {position}");
    }

    private void HandleButtonHoverExit(Entity entity, Vector2 position)
    {
        if (!IsButton(entity)) return;

        ref var button = ref entity.Get<UIButton>();
        if (!button.IsEnabled) return;

        if (entity.Contains<UIPanel>())
        {
            var panelView = new UIPanel.View(entity);
            panelView.BackgroundColor = button.NormalColor;
        }

        Console.WriteLine($"Button hover exit, position: {position}");
    }

    private static bool IsButton(Entity entity)
    {
        return entity.Contains<UIButton>() &&
               entity.Contains<UIElement>() &&
               entity.Contains<UIEventListener>();
    }

    public override void Uninitialize(World world)
    {
        _hoverStates.Clear();
        _sortedUIElements.Clear();
        base.Uninitialize(world);
    }
}