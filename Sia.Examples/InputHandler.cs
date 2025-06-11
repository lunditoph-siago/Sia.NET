using System.Numerics;
using Silk.NET.Input;

namespace Sia_Examples;

public enum UserAction
{
    NavigateUp,
    NavigateDown,
    NavigatePageUp,
    NavigatePageDown,
    NavigateHome,
    NavigateEnd,

    ExecuteAction,
    CancelAction,
    RefreshAction,
    GoBackAction,

    ScrollUp,
    ScrollDown,

    MouseClick,
    MouseMove,

    ExitApplication
}

public readonly record struct UserActionEvent(
    UserAction Action,
    ActionContext Context = default
);

public readonly record struct ActionContext(
    Vector2? MousePosition = null,
    float ScrollDelta = 0f,
    ModifierKeys ModifierKeys = ModifierKeys.None
);

[Flags]
public enum ModifierKeys
{
    None = 0,
    Shift = 1,
    Ctrl = 2,
    Alt = 4
}

public sealed class InputHandler : IDisposable
{
    private readonly HashSet<Key> _pressedKeys = [];
    private IInputContext? _inputContext;
    private bool _disposed;

    public event Action<UserActionEvent>? UserActionTriggered;

    public float ScrollSpeed { get; init; } = 3f;
    public int ScrollLinesPerTick { get; init; } = 3;

    public void Initialize(IInputContext inputContext)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _inputContext = inputContext;

        foreach (var keyboard in inputContext.Keyboards)
        {
            keyboard.KeyDown += OnKeyDown;
            keyboard.KeyUp += OnKeyUp;
        }

        foreach (var mouse in inputContext.Mice)
        {
            mouse.Click += OnMouseClick;
            mouse.Scroll += OnMouseScroll;
            mouse.MouseMove += OnMouseMove;
        }
    }

    private ModifierKeys GetModifierKeys() =>
        (_pressedKeys.Contains(Key.ShiftLeft) || _pressedKeys.Contains(Key.ShiftRight) ? ModifierKeys.Shift : ModifierKeys.None) |
        (_pressedKeys.Contains(Key.ControlLeft) || _pressedKeys.Contains(Key.ControlRight) ? ModifierKeys.Ctrl : ModifierKeys.None) |
        (_pressedKeys.Contains(Key.AltLeft) || _pressedKeys.Contains(Key.AltRight) ? ModifierKeys.Alt : ModifierKeys.None);

    private void OnKeyDown(IKeyboard keyboard, Key key, int scanCode)
    {
        _pressedKeys.Add(key);
        var modifiers = GetModifierKeys();

        var action = key switch
        {
            Key.Escape when modifiers.HasFlag(ModifierKeys.Alt) => UserAction.ExitApplication,
            Key.F4 when modifiers.HasFlag(ModifierKeys.Alt) => UserAction.ExitApplication,

            Key.Up when modifiers.HasFlag(ModifierKeys.Ctrl) => UserAction.NavigatePageUp,
            Key.Down when modifiers.HasFlag(ModifierKeys.Ctrl) => UserAction.NavigatePageDown,
            Key.Up => UserAction.NavigateUp,
            Key.Down => UserAction.NavigateDown,
            Key.PageUp => UserAction.NavigatePageUp,
            Key.PageDown => UserAction.NavigatePageDown,
            Key.Home => UserAction.NavigateHome,
            Key.End => UserAction.NavigateEnd,

            Key.Enter => UserAction.ExecuteAction,
            Key.Escape => UserAction.CancelAction,
            Key.R when !modifiers.HasFlag(ModifierKeys.Ctrl) => UserAction.RefreshAction,
            Key.Backspace => UserAction.GoBackAction,

            _ => (UserAction?)null
        };

        if (action is not null)
        {
            TriggerAction(action.Value, new(ModifierKeys: modifiers));
        }
    }

    private void OnKeyUp(IKeyboard keyboard, Key key, int scanCode)
    {
        _pressedKeys.Remove(key);
    }

    private void OnMouseClick(IMouse mouse, MouseButton button, Vector2 position)
    {
        if (button == MouseButton.Left)
        {
            var context = new ActionContext(
                MousePosition: position,
                ModifierKeys: GetModifierKeys()
            );
            TriggerAction(UserAction.MouseClick, context);
        }
    }

    private void OnMouseMove(IMouse mouse, Vector2 position)
    {
        var context = new ActionContext(
            MousePosition: position,
            ModifierKeys: GetModifierKeys()
        );
        TriggerAction(UserAction.MouseMove, context);
    }

    private void OnMouseScroll(IMouse mouse, ScrollWheel scrollWheel)
    {
        var scrollDirection = scrollWheel.Y > 0 ? UserAction.ScrollUp : UserAction.ScrollDown;
        var scrollAmount = Math.Abs(scrollWheel.Y) * ScrollLinesPerTick;

        var context = new ActionContext(
            ScrollDelta: scrollAmount,
            ModifierKeys: GetModifierKeys()
        );

        TriggerAction(scrollDirection, context);
    }

    private void TriggerAction(UserAction action, ActionContext context = default)
    {
        var actionEvent = new UserActionEvent(action, context);
        UserActionTriggered?.Invoke(actionEvent);
    }

    public void TriggerManualAction(UserAction action, ActionContext context = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        TriggerAction(action, context);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_inputContext is not null)
        {
            foreach (var keyboard in _inputContext.Keyboards)
            {
                keyboard.KeyDown -= OnKeyDown;
                keyboard.KeyUp -= OnKeyUp;
            }

            foreach (var mouse in _inputContext.Mice)
            {
                mouse.Click -= OnMouseClick;
                mouse.Scroll -= OnMouseScroll;
                mouse.MouseMove -= OnMouseMove;
            }
        }

        _pressedKeys.Clear();
        UserActionTriggered = null;
    }
}