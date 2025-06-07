using System.Numerics;
using Silk.NET.Input;
using Silk.NET.OpenGL;

namespace Sia.Examples.Runtime.Addons;

public interface IRenderPass : IDisposable
{
    string Name { get; }
    int Priority { get; }
    bool IsEnabled { get; set; }

    void Initialize(GL gl);
    void Execute(World world, RenderPipeline pipeline);
    void OnResize(int width, int height);
}

public class InputSystem : IAddon
{
    private IInputContext? _inputContext;
    private readonly HashSet<Key> _pressedKeys = [];
    private Vector2 _lastMousePosition;
    private World? _world;

    public Vector2 LastMousePosition => _lastMousePosition;
    public bool IsInitialized => _inputContext != null;

    public void Initialize(IInputContext inputContext)
    {
        _inputContext = inputContext;
        SetupInputHandlers();
    }

    public void OnInitialize(World world)
    {
        _world = world;
    }

    public void OnUninitialize(World world)
    {
        CleanupInputHandlers();
        _world = null;
    }

    private void SetupInputHandlers()
    {
        if (_inputContext == null) return;

        // Setup keyboard input handling
        foreach (var keyboard in _inputContext.Keyboards)
        {
            keyboard.KeyDown += OnKeyDown;
            keyboard.KeyUp += OnKeyUp;
        }

        // Setup mouse input handling
        foreach (var mouse in _inputContext.Mice)
        {
            mouse.Click += OnMouseClick;
            mouse.MouseMove += OnMouseMove;
            mouse.Scroll += OnMouseScroll;
        }
    }

    private void CleanupInputHandlers()
    {
        if (_inputContext == null) return;

        // Cleanup keyboard input handling
        foreach (var keyboard in _inputContext.Keyboards)
        {
            keyboard.KeyDown -= OnKeyDown;
            keyboard.KeyUp -= OnKeyUp;
        }

        // Cleanup mouse input handling
        foreach (var mouse in _inputContext.Mice)
        {
            mouse.Click -= OnMouseClick;
            mouse.MouseMove -= OnMouseMove;
            mouse.Scroll -= OnMouseScroll;
        }

        _pressedKeys.Clear();
    }

    private void OnKeyDown(IKeyboard keyboard, Key key, int scanCode)
    {
        _pressedKeys.Add(key);
        BroadcastEvent(new Components.InputEvents.KeyDown(key));
    }

    private void OnKeyUp(IKeyboard keyboard, Key key, int scanCode)
    {
        _pressedKeys.Remove(key);
        BroadcastEvent(new Components.InputEvents.KeyUp(key));
    }

    private void OnMouseClick(IMouse mouse, MouseButton button, Vector2 position)
    {
        var convertedButton = ConvertMouseButton(button);
        BroadcastEvent(new Components.InputEvents.MouseClick(convertedButton, position));
    }

    private void OnMouseMove(IMouse mouse, Vector2 position)
    {
        _lastMousePosition = position;
        BroadcastEvent(new Components.InputEvents.MouseMove(position));
    }

    private void OnMouseScroll(IMouse mouse, ScrollWheel scrollWheel)
    {
        var scrollDelta = new Vector2(scrollWheel.X, scrollWheel.Y);
        BroadcastEvent(new Components.InputEvents.MouseScroll(scrollDelta));
    }

    private static Components.MouseButton ConvertMouseButton(MouseButton silkButton)
    {
        return silkButton switch
        {
            MouseButton.Left => Components.MouseButton.Left,
            MouseButton.Right => Components.MouseButton.Right,
            MouseButton.Middle => Components.MouseButton.Middle,
            _ => Components.MouseButton.Left
        };
    }

    private void BroadcastEvent<TEvent>(TEvent inputEvent) where TEvent : IEvent
    {
        if (_world == null) return;

        // Send events to all entities that can receive input
        var inputQuery = _world.Query(Matchers.Of<Components.InputReceiver>());

        foreach (var entity in inputQuery)
        {
            ref var inputReceiver = ref entity.Get<Components.InputReceiver>();

            if (!inputReceiver.IsEnabled) continue;

            // Filter events based on input receiver type
            var shouldReceive = inputEvent switch
            {
                Components.InputEvents.KeyDown or Components.InputEvents.KeyUp => inputReceiver.CanReceiveKeyboard,
                Components.InputEvents.MouseClick or Components.InputEvents.MouseMove
                    or Components.InputEvents.MouseScroll => inputReceiver.CanReceiveMouse,
                _ => false
            };

            if (shouldReceive)
                entity.Send(inputEvent);
        }
    }

    public bool IsKeyPressed(Key key) => _pressedKeys.Contains(key);

    public IReadOnlySet<Key> GetPressedKeys() => _pressedKeys;
}