using System.Drawing;
using System.Numerics;
using Silk.NET.Input;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using Silk.NET.Maths;

namespace Sia_Examples;

public class ExampleViewer : IDisposable
{
    private IWindow? _window;
    private GL? _gl;
    private ExampleRunner? _exampleRunner;
    private TextRenderer? _textRenderer;
    private InputHandler? _inputHandler;
    private bool _disposed = false;

    private ViewState _state = new()
    {
        ShowingMenu = true,
        SelectedExample = 0,
        HoveredExample = -1,
        CurrentOutput = string.Empty,
        MenuScrollOffset = 0f,
        OutputScrollOffset = 0f
    };

    private const int HeaderHeight = 120;
    private const int ExampleLineHeight = 2;
    private const int LinesPerPage = 5;

    public ExampleViewer()
    {
        _exampleRunner = new ExampleRunner();
        InitializeWindow();
    }

    private void InitializeWindow()
    {
        var options = WindowOptions.Default with
        {
            Size = new Vector2D<int>(800, 600),
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
        var inputContext = _window!.CreateInput();
        _textRenderer = new TextRenderer(_gl);
        _textRenderer.SetWindowSize(_window!.Size.X, _window.Size.Y);

        _inputHandler = new InputHandler();
        _inputHandler.Initialize(inputContext);
        _inputHandler.UserActionTriggered += HandleUserAction;

        _gl.ClearColor(0.1f, 0.1f, 0.15f, 1.0f);
        _state.MarkDirty();
    }

    private void HandleUserAction(UserActionEvent actionEvent)
    {
        _state = actionEvent.Action switch
        {
            UserAction.NavigateUp => ChangeSelection(-1),
            UserAction.NavigateDown => ChangeSelection(1),
            UserAction.NavigatePageUp => ChangeSelection(-LinesPerPage),
            UserAction.NavigatePageDown => ChangeSelection(LinesPerPage),
            UserAction.NavigateHome => NavigateToHome(),
            UserAction.NavigateEnd => NavigateToEnd(),
            UserAction.ExecuteAction => ExecuteSelectedExample(),
            UserAction.CancelAction => HandleCancel(),
            UserAction.RefreshAction => RefreshCurrentView(),
            UserAction.GoBackAction => GoBackToMenu(),
            UserAction.ScrollUp => HandleScroll(-(int)actionEvent.Context.ScrollDelta),
            UserAction.ScrollDown => HandleScroll((int)actionEvent.Context.ScrollDelta),
            UserAction.MouseClick => HandleMouseClick(actionEvent.Context),
            UserAction.MouseMove => HandleMouseMove(actionEvent.Context),
            UserAction.ExitApplication => ExitApplication(),
            _ => _state
        };
    }

    private ViewState ChangeSelection(int delta)
    {
        if (_state.Mode != ViewMode.Menu) return _state;

        var newIndex = Math.Clamp(_state.SelectedExample + delta, 0, _exampleRunner!.Examples.Count - 1);
        var newState = _state.With(builder => builder.SetSelectedExample(newIndex));

        return EnsureSelectedVisible(newState);
    }

    private ViewState NavigateToHome() => _state.Mode switch
    {
        ViewMode.Menu => EnsureSelectedVisible(_state.With(builder =>
            builder.SetSelectedExample(0).SetScrollOffsets(0f, _state.OutputScrollOffset))),
        ViewMode.Output => ScrollToTop(),
        _ => _state
    };

    private ViewState NavigateToEnd() => _state.Mode switch
    {
        ViewMode.Menu => EnsureSelectedVisible(_state.With(builder =>
            builder.SetSelectedExample(_exampleRunner!.Examples.Count - 1))),
        ViewMode.Output => ScrollToBottom(),
        _ => _state
    };

    private ViewState ExecuteSelectedExample()
    {
        if (_state.Mode != ViewMode.Menu) return _state;

        var output = _exampleRunner!.RunExample(_state.SelectedExample);
        return _state.ToOutputMode(output);
    }

    private ViewState HandleCancel() => _state.Mode switch
    {
        ViewMode.Menu => ExitApplication(),
        ViewMode.Output => _state.ToMenuMode(),
        _ => _state
    };

    private ViewState RefreshCurrentView() => _state.Mode switch
    {
        ViewMode.Output => ExecuteSelectedExample(),
        _ => _state
    };

    private ViewState GoBackToMenu() => _state.Mode switch
    {
        ViewMode.Output => _state.ToMenuMode(),
        _ => _state
    };

    private ViewState HandleScroll(int linesDelta) => _state.Mode switch
    {
        ViewMode.Menu => ScrollMenu(linesDelta),
        ViewMode.Output => ScrollOutput(linesDelta),
        _ => _state
    };

    private ViewState ScrollMenu(int linesDelta)
    {
        var currentScrollLine = (int)(_state.MenuScrollOffset / _textRenderer!.LineHeight);
        var newScrollLine = Math.Max(0, currentScrollLine + linesDelta);

        var totalExamples = _exampleRunner!.Examples.Count;
        var linesPerExample = ExampleLineHeight;
        var totalContentLines = totalExamples * linesPerExample;
        var visibleLines = GetVisibleMenuLines();
        var maxScrollLine = Math.Max(0, totalContentLines - visibleLines + 1);

        newScrollLine = Math.Min(newScrollLine, maxScrollLine);
        var newOffset = newScrollLine * _textRenderer.LineHeight;

        _textRenderer.ScrollOffset = newOffset;
        return _state.With(builder => builder.SetScrollOffsets(newOffset, _state.OutputScrollOffset));
    }

    private ViewState ScrollOutput(int linesDelta)
    {
        var newOffset = _state.OutputScrollOffset + (linesDelta * _textRenderer!.LineHeight);
        _textRenderer.Scroll(linesDelta * _textRenderer.LineHeight);

        return _state.With(builder => builder.SetScrollOffsets(_state.MenuScrollOffset, newOffset));
    }

    private ViewState ScrollToTop() => _state.Mode switch
    {
        ViewMode.Menu => _state.With(builder => builder.SetScrollOffsets(0f, _state.OutputScrollOffset)),
        ViewMode.Output => ScrollToTopOutput(),
        _ => _state
    };

    private ViewState ScrollToTopOutput()
    {
        _textRenderer!.ScrollOffset = 0f;
        return _state.With(builder => builder.SetScrollOffsets(_state.MenuScrollOffset, 0f));
    }

    private ViewState ScrollToBottom() => _state.Mode switch
    {
        ViewMode.Menu => ScrollToBottomMenu(),
        ViewMode.Output => ScrollToBottomOutput(),
        _ => _state
    };

    private ViewState ScrollToBottomMenu()
    {
        var maxScroll = CalculateMaxMenuScroll();
        _textRenderer!.ScrollOffset = maxScroll;
        return _state.With(builder => builder.SetScrollOffsets(maxScroll, _state.OutputScrollOffset));
    }

    private ViewState ScrollToBottomOutput()
    {
        var maxScroll = _textRenderer!.GetTotalLines() * _textRenderer.LineHeight;
        _textRenderer.ScrollOffset = maxScroll;
        return _state.With(builder => builder.SetScrollOffsets(_state.MenuScrollOffset, maxScroll));
    }

    private ViewState HandleMouseClick(ActionContext context)
    {
        if (_state.Mode != ViewMode.Menu || context.MousePosition is not { } position)
            return _state;

        var exampleIndex = GetExampleIndexFromPosition(position);
        if (exampleIndex >= 0 && exampleIndex < _exampleRunner!.Examples.Count)
        {
            var newState = _state.With(builder => builder.SetSelectedExample(exampleIndex));
            var output = _exampleRunner.RunExample(exampleIndex);
            return newState.ToOutputMode(output);
        }

        return _state;
    }

    private ViewState HandleMouseMove(ActionContext context)
    {
        if (_state.Mode != ViewMode.Menu || context.MousePosition is not { } position)
            return _state.With(builder => builder.SetHoveredExample(-1));

        var exampleIndex = GetExampleIndexFromPosition(position);
        var hoveredIndex = (exampleIndex >= 0 && exampleIndex < _exampleRunner!.Examples.Count) ? exampleIndex : -1;

        return _state.With(builder => builder.SetHoveredExample(hoveredIndex));
    }

    private int GetExampleIndexFromPosition(Vector2 position)
    {
        if (position.Y <= HeaderHeight) return -1;

        var relativeY = position.Y - HeaderHeight + _state.MenuScrollOffset;
        var lineHeight = _textRenderer!.LineHeight;
        var linesPerExample = ExampleLineHeight;

        var exampleIndex = (int)(relativeY / (lineHeight * linesPerExample));
        return exampleIndex;
    }

    private ViewState EnsureSelectedVisible(ViewState state)
    {
        if (state.Mode != ViewMode.Menu) return state;

        var selectedLine = state.SelectedExample * ExampleLineHeight;
        var visibleLines = GetVisibleMenuLines();
        var currentScrollLine = (int)(state.MenuScrollOffset / _textRenderer!.LineHeight);

        var newScrollLine = currentScrollLine;

        if (selectedLine < currentScrollLine)
        {
            newScrollLine = selectedLine;
        }
        else if (selectedLine >= currentScrollLine + visibleLines - ExampleLineHeight)
        {
            newScrollLine = selectedLine - visibleLines + ExampleLineHeight;
        }

        newScrollLine = Math.Max(0, newScrollLine);
        var newOffset = newScrollLine * _textRenderer.LineHeight;

        _textRenderer.ScrollOffset = newOffset;
        return state.With(builder => builder.SetScrollOffsets(newOffset, state.OutputScrollOffset));
    }

    private int GetVisibleMenuLines()
    {
        return (_window!.Size.Y - HeaderHeight) / (int)_textRenderer!.LineHeight;
    }

    private float CalculateMaxMenuScroll()
    {
        var totalExamples = _exampleRunner!.Examples.Count;
        var totalContentLines = totalExamples * ExampleLineHeight;
        var visibleLines = GetVisibleMenuLines();
        var maxScrollLines = Math.Max(0, totalContentLines - visibleLines + 1);
        return maxScrollLines * _textRenderer!.LineHeight;
    }

    private ViewState ExitApplication()
    {
        _window?.Close();
        return _state;
    }

    private void UpdateDisplay()
    {
        if (!_state.ConsumeUpdateFlag()) return;

        _textRenderer!.Clear();

        if (_state.Mode == ViewMode.Menu)
            ShowMenu();
        else
            ShowOutput();
    }

    private void ShowMenu()
    {
        _textRenderer!.AddLine("Sia.NET Examples Viewer", Color.Cyan);
        _textRenderer.AddLine("", Color.White);
        _textRenderer.AddLine("Navigation: Arrow Keys, Page Up/Down, Home/End, Mouse Wheel, Click", Color.Yellow);
        _textRenderer.AddLine("Actions: Enter to Run, ESC to Exit", Color.Yellow);
        _textRenderer.AddLine("", Color.White);

        for (int i = 0; i < _exampleRunner!.Examples.Count; i++)
        {
            var example = _exampleRunner.Examples[i];
            var isSelected = i == _state.SelectedExample;
            var isHovered = i == _state.HoveredExample;

            Color nameColor, descColor;
            string prefix;

            if (isSelected)
            {
                nameColor = Color.Yellow;
                descColor = Color.Orange;
                prefix = "► ";
            }
            else if (isHovered)
            {
                nameColor = Color.LightBlue;
                descColor = Color.LightGray;
                prefix = "  ";
            }
            else
            {
                nameColor = Color.White;
                descColor = Color.Gray;
                prefix = "  ";
            }

            _textRenderer.AddLine($"{prefix}{i + 1:D2}. {example.Name}", nameColor);
            _textRenderer.AddLine($"    {example.Description}", descColor);
        }

        _textRenderer.AddLine("", Color.White);
        _textRenderer.AddLine($"Total: {_exampleRunner.Examples.Count}, Selected: {_state.SelectedExample + 1}", Color.Cyan);

        if (_exampleRunner.Examples.Count * ExampleLineHeight > GetVisibleMenuLines())
        {
            var currentScrollLine = (int)(_state.MenuScrollOffset / _textRenderer!.LineHeight);
            var totalScrollableLines = _exampleRunner.Examples.Count * ExampleLineHeight - GetVisibleMenuLines();
            var scrollPercent = totalScrollableLines > 0 ? (float)currentScrollLine / totalScrollableLines : 0f;
            _textRenderer.AddLine($"Scroll: {scrollPercent:P0} (Line {currentScrollLine + 1})", Color.Yellow);
        }
    }

    private void ShowOutput()
    {
        var selectedExample = _exampleRunner!.Examples[_state.SelectedExample];

        _textRenderer!.AddLine($"Running: {selectedExample.Name}", Color.Cyan);
        _textRenderer.AddLine("", Color.White);

        var lines = _state.CurrentOutput.Split('\n', StringSplitOptions.None);
        foreach (var line in lines)
            _textRenderer.AddLine(line, GetLineColor(line));

        _textRenderer.AddLine("", Color.White);
        _textRenderer.AddLine("Controls: ESC/Backspace = Menu, R = Re-run, Mouse Wheel = Scroll, PgUp/PgDn", Color.Yellow);

        if (_textRenderer.GetTotalLines() > _textRenderer.GetVisibleLines())
        {
            var scrollPercent = _textRenderer.ScrollOffset / Math.Max(1, _textRenderer.GetTotalLines() * _textRenderer.LineHeight - (_window!.Size.Y - 60));
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
        _gl!.Clear(ClearBufferMask.ColorBufferBit);
        UpdateDisplay();
        _textRenderer!.Render();
    }

    private void OnResize(Vector2D<int> newSize)
    {
        _textRenderer!.SetWindowSize(newSize.X, newSize.Y);
        _state.MarkDirty();
    }

    private void OnClosing() => Cleanup();

    private void Cleanup()
    {
        if (_disposed) return;

        try
        {
            _textRenderer?.Dispose();
            _exampleRunner?.Dispose();
            _inputHandler?.Dispose();
            _gl?.Dispose();

            _textRenderer = null;
            _exampleRunner = null;
            _inputHandler = null;
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