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

    private IReactiveEntityQuery? _keyboardReceivers;
    private IReactiveEntityQuery? _mouseReceivers;

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

        _keyboardReceivers = world.Query(Matchers.Of<Components.InputReceiver, Components.KeyboardReceiver>());
        _mouseReceivers = world.Query(Matchers.Of<Components.InputReceiver, Components.MouseReceiver>());
    }

    public void OnUninitialize(World world)
    {
        CleanupInputHandlers();
        _keyboardReceivers?.Dispose();
        _mouseReceivers?.Dispose();
        _keyboardReceivers = null;
        _mouseReceivers = null;
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
        BroadcastKeyboardEvent(new Components.InputEvents.KeyDown(key));
    }

    private void OnKeyUp(IKeyboard keyboard, Key key, int scanCode)
    {
        _pressedKeys.Remove(key);
        BroadcastKeyboardEvent(new Components.InputEvents.KeyUp(key));
    }

    private void OnMouseClick(IMouse mouse, MouseButton button, Vector2 position)
    {
        var convertedButton = ConvertMouseButton(button);
        BroadcastMouseEvent(new Components.InputEvents.MouseClick(convertedButton, position));
    }

    private void OnMouseMove(IMouse mouse, Vector2 position)
    {
        _lastMousePosition = position;
        BroadcastMouseEvent(new Components.InputEvents.MouseMove(position));
    }

    private void OnMouseScroll(IMouse mouse, ScrollWheel scrollWheel)
    {
        var scrollDelta = new Vector2(scrollWheel.X, scrollWheel.Y);
        BroadcastMouseEvent(new Components.InputEvents.MouseScroll(scrollDelta));
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

    private void BroadcastKeyboardEvent<TEvent>(TEvent inputEvent) where TEvent : IEvent
    {
        if (_world == null || _keyboardReceivers == null) return;

        foreach (var entity in _keyboardReceivers)
        {
            ref var inputReceiver = ref entity.Get<Components.InputReceiver>();
            ref var keyboardReceiver = ref entity.Get<Components.KeyboardReceiver>();

            if (!inputReceiver.IsEnabled || !keyboardReceiver.IsEnabled) continue;

            var shouldReceive = inputEvent switch
            {
                Components.InputEvents.KeyDown => keyboardReceiver.ReceiveKeyDown,
                Components.InputEvents.KeyUp => keyboardReceiver.ReceiveKeyUp,
                _ => false
            };

            if (shouldReceive)
                entity.Send(inputEvent);
        }
    }

    private void BroadcastMouseEvent<TEvent>(TEvent inputEvent) where TEvent : IEvent
    {
        if (_world == null || _mouseReceivers == null) return;

        foreach (var entity in _mouseReceivers)
        {
            ref var inputReceiver = ref entity.Get<Components.InputReceiver>();
            ref var mouseReceiver = ref entity.Get<Components.MouseReceiver>();

            if (!inputReceiver.IsEnabled || !mouseReceiver.IsEnabled) continue;

            var shouldReceive = inputEvent switch
            {
                Components.InputEvents.MouseClick => mouseReceiver.ReceiveClick,
                Components.InputEvents.MouseMove => mouseReceiver.ReceiveMove,
                Components.InputEvents.MouseScroll => mouseReceiver.ReceiveScroll,
                _ => false
            };

            if (shouldReceive)
                entity.Send(inputEvent);
        }
    }

    public bool IsKeyPressed(Key key) => _pressedKeys.Contains(key);

    public IReadOnlySet<Key> GetPressedKeys() => _pressedKeys;
}