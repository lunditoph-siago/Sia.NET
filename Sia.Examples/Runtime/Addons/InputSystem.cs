using System.Numerics;
using Sia.Examples.Runtime.Components;
using Silk.NET.Input;

namespace Sia.Examples.Runtime.Addons;

public class InputSystem : IAddon
{
    private IInputContext? _inputContext;
    private World? _world;

    public Vector2 LastMousePosition { get; private set; }
    public bool IsInitialized => _inputContext is not null;

    public void OnInitialize(World world)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
    }

    public void OnUninitialize(World world)
    {
        CleanupHandlers();
        _world = null;
    }

    public void Initialize(IInputContext inputContext)
    {
        _inputContext = inputContext ?? throw new ArgumentNullException(nameof(inputContext));
        CreateExistingDevices();
        SetupHandlers();
    }

    #region Setup and Cleanup

    private void CreateExistingDevices()
    {
        if (_inputContext is null) return;
        foreach (var keyboard in _inputContext.Keyboards)
            CreateInputDevice(keyboard.Name);
        foreach (var mouse in _inputContext.Mice)
            CreateInputDevice(mouse.Name);
    }

    private void SetupHandlers()
    {
        if (_inputContext is null) return;

        _inputContext.ConnectionChanged += OnConnectionChanged;

        foreach (var keyboard in _inputContext.Keyboards)
        {
            keyboard.KeyDown += OnKeyDown;
            keyboard.KeyUp += OnKeyUp;
        }

        foreach (var mouse in _inputContext.Mice)
        {
            mouse.MouseDown += OnMouseDown;
            mouse.MouseUp += OnMouseUp;
            mouse.Click += OnMouseClick;
            mouse.DoubleClick += OnMouseDoubleClick;
            mouse.MouseMove += OnMouseMove;
            mouse.Scroll += OnMouseScroll;
        }
    }

    private void CleanupHandlers()
    {
        if (_inputContext is null) return;

        _inputContext.ConnectionChanged -= OnConnectionChanged;

        foreach (var keyboard in _inputContext.Keyboards)
        {
            keyboard.KeyDown -= OnKeyDown;
            keyboard.KeyUp -= OnKeyUp;
        }

        foreach (var mouse in _inputContext.Mice)
        {
            mouse.MouseDown -= OnMouseDown;
            mouse.MouseUp -= OnMouseUp;
            mouse.Click -= OnMouseClick;
            mouse.DoubleClick -= OnMouseDoubleClick;
            mouse.MouseMove -= OnMouseMove;
            mouse.Scroll -= OnMouseScroll;
        }
    }

    #endregion

    #region Device Management

    private void OnConnectionChanged(IInputDevice device, bool isConnected)
    {
        if (_world is null) return;

        if (isConnected)
            CreateInputDevice(device.Name);
        else
            RemoveInputDevice(device.Name);
    }

    private void CreateInputDevice(string deviceId)
    {
        _world!.Create(HList.From(
            new InputDevice(deviceId, true),
            new MouseState(),
            new KeyboardState(),
            new MouseButtonState(),
            new ScrollState()
        ));
    }

    private void RemoveInputDevice(string deviceId)
    {
        var query = _world!.Query(Matchers.Of<InputDevice>());

        foreach (var entity in query)
            if (entity is { IsValid: true } && entity.Get<InputDevice>().DeviceId == deviceId)
            {
                entity.Destroy();
                break;
            }
    }

    #endregion

    #region Keyboard Events

    private void OnKeyDown(IKeyboard keyboard, Key key, int scanCode)
    {
        SendEvent(new InputEvents.KeyPressed(key), keyboard.Name);
    }

    private void OnKeyUp(IKeyboard keyboard, Key key, int scanCode)
    {
        SendEvent(new InputEvents.KeyReleased(key), keyboard.Name);
    }

    #endregion

    #region Mouse Events

    private void OnMouseDown(IMouse mouse, MouseButton button)
    {
        var position = mouse.Position;
        LastMousePosition = position;
        SendEvent(new InputEvents.MouseButtonPressed(button, position), mouse.Name);
    }

    private void OnMouseUp(IMouse mouse, MouseButton button)
    {
        var position = mouse.Position;
        LastMousePosition = position;
        SendEvent(new InputEvents.MouseButtonReleased(button, position), mouse.Name);
    }

    private void OnMouseClick(IMouse mouse, MouseButton button, Vector2 position)
    {
        LastMousePosition = position;
        SendEvent(new InputEvents.Click(position, button), mouse.Name);
    }

    private void OnMouseDoubleClick(IMouse mouse, MouseButton button, Vector2 position)
    {
        LastMousePosition = position;
        SendEvent(new InputEvents.DoubleClick(position, button), mouse.Name);
    }

    private void OnMouseMove(IMouse mouse, Vector2 position)
    {
        var delta = position - LastMousePosition;
        LastMousePosition = position;
        SendEvent(new InputEvents.MouseMoved(position, delta), mouse.Name);
    }

    private void OnMouseScroll(IMouse mouse, ScrollWheel scrollWheel)
    {
        var scrollDelta = new Vector2(scrollWheel.X, scrollWheel.Y);
        SendEvent(new InputEvents.MouseScrolled(scrollDelta, LastMousePosition), mouse.Name);
    }

    #endregion

    #region Event Dispatch

    private void SendEvent<T>(T inputEvent, string deviceId) where T : IEvent
    {
        GetInputDevice(deviceId)?.Send(inputEvent);
    }

    private Entity? GetInputDevice(string deviceId)
    {
        if (_world is null) return null;

        var query = _world.Query(Matchers.Of<InputDevice>());

        foreach (var entity in query)
            if (entity is { IsValid: true } && entity.Get<InputDevice>().DeviceId == deviceId)
                return entity;

        return null;
    }

    #endregion
}