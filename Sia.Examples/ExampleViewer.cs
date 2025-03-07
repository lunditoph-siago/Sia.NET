using System.Drawing;
using Silk.NET.Input;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using Silk.NET.Maths;

namespace Sia_Examples;

public class ExampleViewer : IDisposable
{
    private IWindow? _window;
    private GL? _gl;
    private IInputContext? _inputContext;
    private ExampleRunner? _exampleRunner;
    private TextRenderer? _textRenderer;

    private int _selectedExample = 0;
    private bool _showingMenu = true;
    private string _currentOutput = "";
    private bool _needsUpdate = true;
    private bool _disposed = false;

    private float _menuScrollOffset = 0f;
    private float _outputScrollOffset = 0f;
    private const float ScrollSpeed = 30f;

    public ExampleViewer()
    {
        _exampleRunner = new ExampleRunner();
        InitializeWindow();
    }

    private void InitializeWindow()
    {
        var options = WindowOptions.Default with
        {
            Size = new Vector2D<int>(1400, 900),
            Title = "Sia.NET Examples Viewer"
        };

        _window = Window.Create(options);
        _window.Load += OnLoad;
        _window.Render += OnRender;
        _window.Closing += OnClosing;
        _window.Resize += OnResize;
    }

    private void OnLoad()
    {
        _gl = GL.GetApi(_window!);
        _inputContext = _window!.CreateInput();
        _textRenderer = new TextRenderer(_gl);

        foreach (var keyboard in _inputContext.Keyboards)
            keyboard.KeyDown += OnKeyDown;

        foreach (var mouse in _inputContext.Mice)
        {
            mouse.Click += OnMouseClick;
            mouse.Scroll += OnMouseScroll;
        }

        _gl.ClearColor(0.1f, 0.1f, 0.15f, 1.0f);
        UpdateDisplay();
    }

    private void OnKeyDown(IKeyboard keyboard, Key key, int scanCode)
    {
        if (_showingMenu)
            HandleMenuInput(key);
        else
            HandleOutputInput(key);
    }

    private void HandleMenuInput(Key key)
    {
        switch (key)
        {
            case Key.Up:
                ChangeSelection(-1);
                break;
            case Key.Down:
                ChangeSelection(1);
                break;
            case Key.PageUp:
                ChangeSelection(-5);
                break;
            case Key.PageDown:
                ChangeSelection(5);
                break;
            case Key.Home:
                _selectedExample = 0;
                ResetScroll();
                break;
            case Key.End:
                _selectedExample = _exampleRunner.Examples.Count - 1;
                EnsureSelectedVisible();
                break;
            case Key.Enter:
                RunSelectedExample();
                break;
            case Key.Escape:
                _window.Close();
                break;
        }
    }

    private void HandleOutputInput(Key key)
    {
        switch (key)
        {
            case Key.Escape:
            case Key.Backspace:
                _showingMenu = true;
                ResetScroll();
                break;
            case Key.R:
                RunSelectedExample();
                break;
            case Key.Home:
                ResetScroll();
                break;
            case Key.End:
                ScrollToEnd();
                break;
            case Key.PageUp:
                ScrollOutput(-_textRenderer.GetVisibleLines() * _textRenderer.LineHeight);
                break;
            case Key.PageDown:
                ScrollOutput(_textRenderer.GetVisibleLines() * _textRenderer.LineHeight);
                break;
        }
    }

    private void ChangeSelection(int delta)
    {
        _selectedExample = Math.Clamp(_selectedExample + delta, 0, _exampleRunner.Examples.Count - 1);
        EnsureSelectedVisible();
        _needsUpdate = true;
    }

    private void ResetScroll()
    {
        _menuScrollOffset = _outputScrollOffset = 0;
        _textRenderer.ScrollOffset = 0;
        _needsUpdate = true;
    }

    private void ScrollToEnd()
    {
        _outputScrollOffset = _textRenderer.GetTotalLines() * _textRenderer.LineHeight;
        _textRenderer.ScrollOffset = _outputScrollOffset;
        _needsUpdate = true;
    }

    private void OnMouseClick(IMouse mouse, MouseButton button, System.Numerics.Vector2 position)
    {
        if (button != MouseButton.Left || !_showingMenu) return;

        var headerHeight = 120;
        var lineHeight = _textRenderer.LineHeight * 2;

        if (position.Y > headerHeight)
        {
            int clickedIndex = (int)((position.Y - headerHeight + _menuScrollOffset) / lineHeight);
            if (clickedIndex >= 0 && clickedIndex < _exampleRunner.Examples.Count)
            {
                _selectedExample = clickedIndex;
                RunSelectedExample();
            }
        }
    }

    private void OnMouseScroll(IMouse mouse, ScrollWheel scrollWheel)
    {
        float scrollDelta = scrollWheel.Y * ScrollSpeed;

        if (_showingMenu)
            ScrollMenu(-scrollDelta);
        else
            ScrollOutput(-scrollDelta);
    }

    private void ScrollMenu(float delta)
    {
        _menuScrollOffset += delta;

        float totalHeight = _exampleRunner.Examples.Count * _textRenderer.LineHeight * 2;
        float visibleHeight = _window.Size.Y - 120;
        float maxScroll = Math.Max(0, totalHeight - visibleHeight);

        _menuScrollOffset = Math.Clamp(_menuScrollOffset, 0, maxScroll);
        _textRenderer.ScrollOffset = _menuScrollOffset;
        _needsUpdate = true;
    }

    private void ScrollOutput(float delta)
    {
        _outputScrollOffset += delta;
        _textRenderer.Scroll(delta);
        _needsUpdate = true;
    }

    private void EnsureSelectedVisible()
    {
        if (!_showingMenu) return;

        float selectedY = _selectedExample * _textRenderer.LineHeight * 2;
        float visibleHeight = _window.Size.Y - 120;

        if (selectedY < _menuScrollOffset)
            _menuScrollOffset = selectedY;
        else if (selectedY > _menuScrollOffset + visibleHeight - _textRenderer.LineHeight * 2)
            _menuScrollOffset = selectedY - visibleHeight + _textRenderer.LineHeight * 2;

        _textRenderer.ScrollOffset = _menuScrollOffset;
    }

    private void RunSelectedExample()
    {
        _showingMenu = false;
        ResetScroll();
        _currentOutput = _exampleRunner.RunExample(_selectedExample);
        _needsUpdate = true;
    }

    private void UpdateDisplay()
    {
        if (!_needsUpdate) return;

        _textRenderer.Clear();

        if (_showingMenu)
            ShowMenu();
        else
            ShowOutput();

        _needsUpdate = false;
    }

    private void ShowMenu()
    {
        _textRenderer.AddLine("Sia.NET Examples Viewer", Color.Cyan);
        _textRenderer.AddLine("", Color.White);
        _textRenderer.AddLine("Navigation: Arrow Keys, Page Up/Down, Home/End, Mouse Wheel, Click", Color.Yellow);
        _textRenderer.AddLine("Actions: Enter to Run, ESC to Exit", Color.Yellow);
        _textRenderer.AddLine("", Color.White);

        for (int i = 0; i < _exampleRunner.Examples.Count; i++)
        {
            var example = _exampleRunner.Examples[i];
            var isSelected = i == _selectedExample;

            var (nameColor, descColor, prefix) = isSelected
                ? (Color.Black, Color.DarkGray, "> ")
                : (Color.White, Color.Gray, "  ");

            _textRenderer.AddLine($"{prefix}{i + 1:D2}. {example.Name}", nameColor);
            _textRenderer.AddLine($"    {example.Description}", descColor);
        }

        _textRenderer.AddLine("", Color.White);
        _textRenderer.AddLine($"Total: {_exampleRunner.Examples.Count}, Selected: {_selectedExample + 1}", Color.Cyan);

        if (_exampleRunner.Examples.Count > _textRenderer.GetVisibleLines() / 2)
        {
            var scrollPercent = _menuScrollOffset / Math.Max(1, _exampleRunner.Examples.Count * _textRenderer.LineHeight * 2 - (_window.Size.Y - 120));
            _textRenderer.AddLine($"Scroll: {scrollPercent:P0}", Color.Yellow);
        }
    }

    private void ShowOutput()
    {
        var selectedExample = _exampleRunner.Examples[_selectedExample];

        _textRenderer.AddLine($"Running: {selectedExample.Name}", Color.Cyan);
        _textRenderer.AddLine("", Color.White);

        var lines = _currentOutput.Split('\n', StringSplitOptions.None);
        foreach (var line in lines)
            _textRenderer.AddLine(line, GetLineColor(line));

        _textRenderer.AddLine("", Color.White);
        _textRenderer.AddLine("Controls: ESC/Backspace = Menu, R = Re-run, Mouse Wheel = Scroll, PgUp/PgDn", Color.Yellow);

        if (_textRenderer.GetTotalLines() > _textRenderer.GetVisibleLines())
        {
            var scrollPercent = _textRenderer.ScrollOffset / Math.Max(1, _textRenderer.GetTotalLines() * _textRenderer.LineHeight - (_window.Size.Y - 60));
            _textRenderer.AddLine($"Scroll: {scrollPercent:P0} ({_textRenderer.GetTotalLines()} lines)", Color.Gray);
        }
    }

    private static Color GetLineColor(string line) => line switch
    {
        _ when line.Contains("==") && (line.Contains("Running") || line.Contains("Example")) => Color.Cyan,
        _ when line.Contains("Error") || line.Contains("Exception") => Color.Red,
        _ when line.Contains("Dead") || line.Contains("Destroyed") => Color.Red,
        _ when line.Contains("HP") || line.Contains("Health") || line.Contains("Heal") => Color.Green,
        _ when line.Contains("Position") || line.Contains("Move") || line.Contains("Rotation") => Color.Yellow,
        _ when line.Contains("Damage") || line.Contains("Attack") => Color.Orange,
        _ when line.Contains("Entity") || line.Contains("Component") => Color.LightBlue,
        _ when line.Contains("System") || line.Contains("Update") => Color.Magenta,
        _ when line.Contains("Success") || line.Contains("Complete") => Color.LightGreen,
        _ => Color.White
    };

    private void OnRender(double deltaTime)
    {
        _gl.Clear(ClearBufferMask.ColorBufferBit);
        UpdateDisplay();
        _textRenderer.Render();
    }

    private void OnResize(Vector2D<int> newSize)
    {
        _textRenderer.SetWindowSize(newSize.X, newSize.Y);
        _needsUpdate = true;
    }

    private void OnClosing() => Cleanup();

    private void Cleanup()
    {
        if (_disposed) return;

        try
        {
            _textRenderer?.Dispose();
            _exampleRunner?.Dispose();
            _inputContext?.Dispose();
            _gl?.Dispose();

            _textRenderer = null;
            _exampleRunner = null;
            _inputContext = null;
            _gl = null;
        }
        catch (ObjectDisposedException) { }
    }

    public void Run() => _window?.Run();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Cleanup();

        try
        {
            _window?.Dispose();
            _window = null;
        }
        catch (ObjectDisposedException) { }
    }
}