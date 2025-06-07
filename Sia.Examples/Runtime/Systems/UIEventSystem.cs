using System.Numerics;
using Sia.Examples.Runtime.Components;

namespace Sia.Examples.Runtime.Systems;

public class UIEventSystem : EventSystemBase
{
    private Vector2 _lastMousePosition;
    private readonly Dictionary<Entity, bool> _hoverStates = new();

    public override void Initialize(World world)
    {
        base.Initialize(world);
        
        RecordEvent<InputEvents.MouseClick>();
        RecordEvent<InputEvents.MouseMove>();
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
        }
    }

    private void HandleMouseClick(InputEvents.MouseClick mouseClick)
    {
        var mousePos = mouseClick.Position;
        var button = mouseClick.Button;
        
        // Get all interactive UI elements, sorted by layer
        var uiQuery = World.Query(Matchers.Of<UIElement, UIEventListener>());
        var sortedElements = new List<(Entity entity, int layer)>();

        foreach (var entity in uiQuery)
        {
            ref var uiElement = ref entity.Get<UIElement>();
            if (uiElement.IsVisible && uiElement.IsInteractable)
            {
                sortedElements.Add((entity, uiElement.Layer));
            }
        }

        // Sort by layer (higher layer first)
        sortedElements.Sort((a, b) => b.layer.CompareTo(a.layer));

        // Find the first clicked UI element
        foreach (var (entity, _) in sortedElements)
        {
            ref var uiElement = ref entity.Get<UIElement>();
            ref var eventListener = ref entity.Get<UIEventListener>();

            if (!eventListener.IsEnabled || !eventListener.ListenToClick)
                continue;

            if (uiElement.Contains(mousePos))
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
        var mousePos = mouseMove.Position;
        _lastMousePosition = mousePos;

        // Get all interactive UI elements, sorted by layer
        var uiQuery = World.Query(Matchers.Of<UIElement, UIEventListener>());
        var sortedElements = new List<(Entity entity, int layer)>();

        foreach (var entity in uiQuery)
        {
            ref var uiElement = ref entity.Get<UIElement>();
            if (uiElement.IsVisible && uiElement.IsInteractable)
            {
                sortedElements.Add((entity, uiElement.Layer));
            }
        }

        // Sort by layer (higher layer first)
        sortedElements.Sort((a, b) => b.layer.CompareTo(a.layer));

        Entity? currentHoveredEntity = null;

        // Find the topmost hovered element
        foreach (var (entity, _) in sortedElements)
        {
            ref var uiElement = ref entity.Get<UIElement>();
            ref var eventListener = ref entity.Get<UIEventListener>();

            if (!eventListener.IsEnabled || !eventListener.ListenToHover)
                continue;

            if (uiElement.Contains(mousePos))
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
                // Exit hover state
                var entity = kvp.Key;
                entitiesToRemove.Add(entity);

                if (entity.Contains<UIInteractionState>())
                {
                    var stateView = new UIInteractionState.View(entity);
                    stateView.IsHovered = false;
                    stateView.IsPressed = false;
                }

                // Handle button hover exit logic
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

                // Handle button hover enter logic
                HandleButtonHoverEnter(currentHoveredEntity, mousePos);

                World.Send(currentHoveredEntity, new UIHoverEnterEvent(currentHoveredEntity, mousePos));
            }
        }
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

        // Update button color to hover color
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

        // Restore button color to normal color
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
        base.Uninitialize(world);
    }
}