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

    private void ChangeSelection(int delta)
    {
        if (_state is not ViewState.Menu menu) return;

        var newIndex = Math.Clamp(menu.SelectedExample + delta, 0,
            _exampleRunner!.Examples.Count - 1);
        _state = menu.WithSelection(newIndex);
        
        // Update UI to reflect the selection change
        if (_state.IsMenu())
            ShowMenu();
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
        Runtime.UI.Text(_world, new Vector2(50, 550), "Sia.NET Example Viewer", Color.Cyan, 24f);

        // Create navigation instructions  
        Runtime.UI.Text(_world, new Vector2(50, 520), "Navigation: Arrow keys, Execute: Enter, Exit: ESC", Color.Yellow, 14f);

        // Create scrollable menu list
        var menuList = CreateScrollableMenuList(menu);

        // Create statistics information
        Runtime.UI.Text(_world, new Vector2(50, 30), 
            $"Total: {_exampleRunner.Examples.Count}, Selected: {menu.SelectedExample + 1}", Color.Cyan, 12f);
    }

    private Entity CreateScrollableMenuList(ViewState.Menu menu)
    {
        if (_world == null || _exampleRunner == null) return default;

        // Create scrollable menu container - fixed menu overflow issue
        var menuContainer = Runtime.UI.ScrollableMenu(_world, new Vector2(20, 70), new Vector2(760, 430));

        // Generate menu content text
        var contentLines = new List<string>();
        for (int i = 0; i < _exampleRunner.Examples.Count; i++)
        {
            var example = _exampleRunner.Examples[i];
            var isSelected = i == menu.SelectedExample;
            var isHovered = i == menu.HoveredExample;

            var prefix = (isSelected, isHovered) switch
            {
                (true, _) => "► ",
                (false, true) => "  ",
                _ => "  "
            };

            contentLines.Add($"{prefix}{i + 1:D2}. {example.Name}");
            contentLines.Add($"    {example.Description}");
            contentLines.Add(""); // Empty line for spacing
        }

        // Set content to scrollable menu
        var content = string.Join("\n", contentLines);
        Runtime.UI.SetContent(menuContainer, content);

        // Auto-scroll to selected item
        ScrollToSelectedItem(menuContainer, menu.SelectedExample);

        return menuContainer;
    }

    private void ScrollToSelectedItem(Entity scrollableEntity, int selectedIndex)
    {
        if (!scrollableEntity.Contains<UIScrollable>()) return;

        const float itemHeight = 60f; // Each item takes about 3 lines * 20px
        var targetY = selectedIndex * itemHeight;
        var viewportHeight = scrollableEntity.Get<UIElement>().Size.Y;
        
        // Center the selected item in viewport
        var scrollOffset = new Vector2(0, Math.Max(0, targetY - viewportHeight * 0.4f));
        Runtime.UI.ScrollTo(scrollableEntity, scrollOffset);
    }

    private void CreateOutputUI()
    {
        if (_world == null || _exampleRunner == null || !_state.IsOutput()) return;

        var output = _state.AsOutput();
        var selectedExample = _exampleRunner.Examples[output.SelectedExample];

        // Create title
        Runtime.UI.Text(_world, new Vector2(50, 550), 
            $"Execution Result: {selectedExample.Name}", Color.Cyan, 20f);

        // Create control instructions
        Runtime.UI.Text(_world, new Vector2(50, 520), 
            "ESC=Return to menu, R=Re-run, Scroll wheel=Scroll", Color.Yellow, 14f);

        // Create background panel
        Runtime.UI.Panel(_world, new Vector2(20, 50), new Vector2(760, 450), 
            Color.FromArgb(60, 20, 20, 30));

        // Create scrollable output text area
        Runtime.UI.ScrollableText(_world, new Vector2(40, 70), new Vector2(720, 410),
            output.Content, Color.White, 11f, Color.FromArgb(60, 20, 20, 30));
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