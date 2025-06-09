using System.Drawing;
using System.Numerics;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using Silk.NET.Maths;
using Silk.NET.Input;
using Sia.Examples.Runtime.Addons;
using Sia.Examples.Runtime.Components;
using Sia.Examples.Runtime.Systems;

namespace Sia.Examples;

public class ExampleViewer : IDisposable
{
    private IWindow? _window;
    private GL? _gl;
    private World? _world;
    private SystemStage? _systemStage;
    private RenderPipeline? _renderPipeline;
    private InputSystem? _inputAddon;
    private ExampleRunner? _exampleRunner;
    private IInputContext? _inputContext;
    private bool _disposed = false;

    // Application state
    private ViewState _state = ViewState.InitialMenu();

    private int _frameCount = 0;
    private double _totalTime = 0;

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
            Title = "Sia.NET Examples"
        };

        _window = Window.Create(options);
        _window.Load += OnLoad;
        _window.Render += OnRender;
        _window.Closing += OnClosing;
        _window.Resize += OnResize;
    }

    private void OnLoad()
    {
        Console.WriteLine("[ExampleViewer] Initialization started");

        // Initialize OpenGL and ECS
        _gl = GL.GetApi(_window!);
        _inputContext = _window!.CreateInput();

        _world = new World();
        Context<World>.Current = _world;

        // Setup render pipeline
        _renderPipeline = _world.AcquireAddon<RenderPipeline>();
        _renderPipeline.Initialize(_gl, _window!.Size.X, _window!.Size.Y);

        // Setup input system
        _inputAddon = _world.AcquireAddon<InputSystem>();
        _inputAddon.Initialize(_inputContext);

        // Create UI event system
        _systemStage = SystemChain.Empty
            .Add<UIEventSystem>()
            .Add<UIScrollSystem>()
            .CreateStage(_world);

        // Register input event handlers
        RegisterInputHandlers();

        // Initially show menu
        ShowMenu();

        Console.WriteLine("[ExampleViewer] Initialization completed!");
    }

    private void RegisterInputHandlers()
    {
        if (_inputContext == null) return;

        foreach (var keyboard in _inputContext.Keyboards)
        {
            keyboard.KeyDown += OnKeyDown;
        }

        foreach (var mouse in _inputContext.Mice)
        {
            mouse.Click += OnMouseClick;
            mouse.Scroll += OnMouseScroll;
            mouse.MouseMove += OnMouseMove;
        }
    }

    private void OnKeyDown(IKeyboard keyboard, Key key, int scanCode)
    {
        if (_state.IsMenu())
        {
            HandleMenuKeyInput(key);
        }
        else
        {
            HandleOutputKeyInput(key);
        }
    }

    private void HandleMenuKeyInput(Key key)
    {
        if (_state is not ViewState.Menu menu) return;

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
                _state = menu.WithSelection(0);
                break;
            case Key.End:
                _state = menu.WithSelection(_exampleRunner!.Examples.Count - 1);
                break;
            case Key.Enter:
                ExecuteSelectedExample();
                break;
            case Key.Escape:
                _window?.Close();
                break;
        }
    }

    private void HandleOutputKeyInput(Key key)
    {
        switch (key)
        {
            case Key.Escape:
            case Key.Backspace:
                ReturnToMenu();
                break;
            case Key.R:
                RefreshCurrentExample();
                break;
        }
    }

    private void OnMouseClick(IMouse mouse, Silk.NET.Input.MouseButton button, Vector2 position)
    {
        if (!_state.IsMenu() || button != Silk.NET.Input.MouseButton.Left) return;

        var menu = _state.AsMenu();
        var exampleIndex = GetExampleIndexFromPosition(position);
        if (exampleIndex >= 0 && exampleIndex < _exampleRunner!.Examples.Count)
        {
            _state = menu.WithSelection(exampleIndex);
            ExecuteSelectedExample();
        }
    }

    private void OnMouseMove(IMouse mouse, Vector2 position)
    {
        if (!_state.IsMenu()) return;

        var menu = _state.AsMenu();
        var exampleIndex = GetExampleIndexFromPosition(position);
        var hoveredIndex = (exampleIndex >= 0 && exampleIndex < _exampleRunner!.Examples.Count)
            ? exampleIndex : -1;

        _state = menu.WithHover(hoveredIndex);
    }

    private void OnMouseScroll(IMouse mouse, ScrollWheel scrollWheel)
    {
        var scrollDelta = scrollWheel.Y * 20f;

        _state = _state switch
        {
            ViewState.Menu menu => menu.WithScroll(scrollDelta),
            ViewState.Output output => output.WithScroll(scrollDelta),
            _ => _state
        };
    }

    private int GetExampleIndexFromPosition(Vector2 position)
    {
        // Simple position to index mapping
        const int headerHeight = 120;
        const int itemHeight = 40;

        if (position.Y <= headerHeight) return -1;

        var relativeY = position.Y - headerHeight;
        var index = (int)(relativeY / itemHeight);

        return index;
    }

    private void ChangeSelection(int delta)
    {
        if (_state is not ViewState.Menu menu) return;

        var newIndex = Math.Clamp(menu.SelectedExample + delta, 0,
            _exampleRunner!.Examples.Count - 1);
        _state = menu.WithSelection(newIndex);
    }

    private void ExecuteSelectedExample()
    {
        if (_exampleRunner == null || !_state.IsMenu()) return;

        var menu = _state.AsMenu();
        var output = _exampleRunner.RunExample(menu.SelectedExample);
        _state = _state.ToOutput(output);
        ShowOutput();
    }

    private void RefreshCurrentExample()
    {
        if (_exampleRunner == null || !_state.IsOutput()) return;

        var output = _state.AsOutput();
        var newOutput = _exampleRunner.RunExample(output.SelectedExample);
        _state = output.WithContent(newOutput);
        ShowOutput();
    }

    private void ReturnToMenu()
    {
        _state = _state.ToMenu();
        ShowMenu();
    }

    private void ShowMenu()
    {
        ClearUI();
        CreateMenuUI();
    }

    private void ShowOutput()
    {
        ClearUI();
        CreateOutputUI();
    }

    private void ClearUI()
    {
        if (_world == null) return;

        List<Entity> entitiesToRemove = [];
        var uiQuery = _world.Query(Matchers.Of<UIElement>());
        foreach (var entity in uiQuery)
        {
            entitiesToRemove.Add(entity);
        }

        foreach (var entity in entitiesToRemove)
        {
            entity.Destroy();
        }
    }

    private void CreateMenuUI()
    {
        if (_world == null || _exampleRunner == null || !_state.IsMenu()) return;

        var menu = _state.AsMenu();

        // Create title
        _world.Create(HList.From(
            new UIElement(new Vector2(50, 550), new Vector2(700, 30), true, false, 1),
            new UIText("Sia.NET Example Viewer", Color.Cyan, 24f, true),
            new RenderLayer(1, true)
        ));

        // Create navigation instructions
        _world.Create(HList.From(
            new UIElement(new Vector2(50, 520), new Vector2(700, 20), true, false, 1),
            new UIText("Navigation: Arrow keys, Execute: Enter, Exit: ESC", Color.Yellow, 14f, true),
            new RenderLayer(1, true)
        ));

        // Create background panel
        _world.Create(HList.From(
            new UIElement(new Vector2(20, 50), new Vector2(760, 450), true, false, 0),
            new UIPanel(Color.FromArgb(60, 30, 30, 40), true),
            new RenderLayer(0, true)
        ));

        // Create example list
        for (int i = 0; i < _exampleRunner.Examples.Count; i++)
        {
            var example = _exampleRunner.Examples[i];
            var yPos = 480 - i * 40;
            var isSelected = i == menu.SelectedExample;
            var isHovered = i == menu.HoveredExample;

            var (nameColor, descColor, prefix) = (isSelected, isHovered) switch
            {
                (true, _) => (Color.Yellow, Color.Orange, "► "),
                (false, true) => (Color.LightBlue, Color.LightGray, "  "),
                _ => (Color.White, Color.Gray, "  ")
            };

            // Create example name
            _world.Create(HList.From(
                new UIElement(new Vector2(60, yPos + 8), new Vector2(400, 20), true, false, 2),
                new UIText($"{prefix}{i + 1:D2}. {example.Name}", nameColor, 14f, true),
                new RenderLayer(2, true)
            ));

            // Create example description
            _world.Create(HList.From(
                new UIElement(new Vector2(80, yPos - 8), new Vector2(500, 15), true, false, 2),
                new UIText($"    {example.Description}", descColor, 11f, true),
                new RenderLayer(2, true)
            ));
        }

        // Create statistics information
        _world.Create(HList.From(
            new UIElement(new Vector2(50, 30), new Vector2(400, 15), true, false, 1),
            new UIText($"Total: {_exampleRunner.Examples.Count}, Selected: {menu.SelectedExample + 1}",
                Color.Cyan, 12f, true),
            new RenderLayer(1, true)
        ));
    }

    private void CreateOutputUI()
    {
        if (_world == null || _exampleRunner == null || !_state.IsOutput()) return;

        var output = _state.AsOutput();
        var selectedExample = _exampleRunner.Examples[output.SelectedExample];

        // Create title
        _world.Create(HList.From(
            new UIElement(new Vector2(50, 550), new Vector2(700, 30), true, false, 1),
            new UIText($"Execution Result: {selectedExample.Name}", Color.Cyan, 20f, true),
            new RenderLayer(1, true)
        ));

        // Create control instructions
        _world.Create(HList.From(
            new UIElement(new Vector2(50, 520), new Vector2(700, 20), true, false, 1),
            new UIText("ESC=Return to menu, R=Re-run, Scroll wheel=Scroll", Color.Yellow, 14f, true),
            new RenderLayer(1, true)
        ));

        // Create background panel
        _world.Create(HList.From(
            new UIElement(new Vector2(20, 50), new Vector2(760, 450), true, false, 0),
            new UIPanel(Color.FromArgb(60, 20, 20, 30), true),
            new RenderLayer(0, true)
        ));

        // Create scrollable output text area
        Runtime.UI.ScrollableText(
            _world,
            new Vector2(40, 70),
            new Vector2(720, 410),
            output.Content,
            Color.White,
            11f,
            Color.FromArgb(60, 20, 20, 30)
        );
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

    private ViewState _lastState = ViewState.InitialMenu();

    private void OnRender(double deltaTime)
    {
        _frameCount++;
        _totalTime += deltaTime;

        if (_world == null || _renderPipeline == null || _systemStage == null)
            return;

        try
        {
            // Update display (if state has changed)
            if (!_state.Equals(_lastState))
            {
                _lastState = _state;

                if (_state.IsMenu())
                    ShowMenu();
                else
                    ShowOutput();
            }

            // Update performance display
            if (_frameCount % 30 == 0)
                UpdatePerformanceDisplay();

            // Execute UI systems
            _systemStage.Tick();

            // Render
            _renderPipeline.BeginFrame((float)deltaTime);
            _renderPipeline.EndFrame();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ModernExampleViewer] Render exception: {ex.Message}");
        }
    }

    private void UpdatePerformanceDisplay()
    {
        if (_world == null) return;

        var fps = _totalTime > 0 ? _frameCount / _totalTime : 0;
        var perfText = $"FPS: {fps:F1} | Entities: {_world.Count} | Frames: {_frameCount}";

        // Performance display update logic can be added here
        // Omitted for simplification
    }

    private void OnResize(Vector2D<int> newSize)
    {
        _renderPipeline?.Resize(newSize.X, newSize.Y);
        // Force UI refresh by creating a new state instance
        _state = _state switch
        {
            ViewState.Menu menu => menu with { },
            ViewState.Output output => output with { },
            _ => _state
        };
    }

    private void OnClosing()
    {
        Console.WriteLine("[ExampleViewer] Closing example viewer...");
        Cleanup();
    }

    private void Cleanup()
    {
        if (_disposed) return;

        try
        {
            _systemStage?.Dispose();
            _world?.Dispose();
            _exampleRunner?.Dispose();
            _gl?.Dispose();
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