using System.Drawing;
using System.Numerics;
using Sia.Examples.Runtime.Addons;
using Sia.Examples.Runtime.Components;
using Sia.Examples.Runtime.Systems;
using Sia.Reactors;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;

namespace Sia.Examples.Runtime;

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

    // UI Entities
    private Entity? _rootContainer;
    private Entity? _currentView;

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
        _gl = GL.GetApi(_window!);
        _inputContext = _window!.CreateInput();

        _world = new World();
        Context<World>.Current = _world;

        _renderPipeline = _world.AcquireAddon<RenderPipeline>();
        _renderPipeline.Initialize(_gl, _window!.Size.X, _window!.Size.Y);

        _inputAddon = _world.AcquireAddon<InputSystem>();
        _inputAddon.Initialize(_inputContext);

        _world.AcquireAddon<Hierarchy<UIHierarchyTag>>();

        _systemStage = SystemChain.Empty
            .Add<KeyboardStateSystem>()
            .Add<MouseStateSystem>()
            .Add<MouseButtonStateSystem>()
            .Add<MouseScrollStateSystem>()
            .Add<UIClickHitTestSystem>()
            .Add<UIHoverStateSystem>()
            .Add<UIButtonInteractionSystem>()
            .Add<UIScrollInteractionSystem>()
            .Add<UILayoutSystem>()
            .Add<UIScrollContentSizeSystem>()
            .Add<UIVisibilitySystem>()
            .CreateStage(_world);

        CreateRootContainer();
        RegisterInputHandlers();
        ShowMenu();
    }

    private void CreateRootContainer()
    {
        if (_world == null) return;

        _rootContainer = UIFactory.CreatePanel(_world, Vector2.Zero, new Vector2(800, 600));
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
        ClearCurrentView();
        CreateMenuView();
    }

    private void ShowOutput()
    {
        ClearCurrentView();
        CreateOutputView();
    }

    private void ClearCurrentView()
    {
        if (_currentView?.IsValid == true)
        {
            _currentView.Destroy();
            _currentView = null;
        }
    }

    private static bool IsValidParent(Entity? entity)
    {
        return entity?.IsValid == true && entity.Contains<UIElement>();
    }

    private void CreateMenuView()
    {
        if (_world == null || _exampleRunner == null || !_state.IsMenu()) return;
        if (!IsValidParent(_rootContainer)) return;

        var menu = _state.AsMenu();

        _currentView = UIFactory.CreateVStackLayout(_world, new Vector2(20, 20), 10f, _rootContainer);
        if (!IsValidParent(_currentView)) return;

        // Title
        UIFactory.CreateText(_world, Vector2.Zero, "Sia.NET Examples", Color.Cyan, 24f, _currentView);

        // Navigation
        UIFactory.CreateText(_world, Vector2.Zero, "Navigation: ↑↓ Select, Enter=Run, ESC=Exit",
            Color.Yellow, 14f, _currentView);

        // Example list with validation
        var listContainer = UIFactory.CreateScrollView(_world, Vector2.Zero, new Vector2(760, 400), 
            ScrollDirection.Vertical, _currentView);
        if (IsValidParent(listContainer))
        {
            CreateExampleList(listContainer, menu);
        }

        // Stats
        UIFactory.CreateText(_world, Vector2.Zero,
            $"Total: {_exampleRunner.Examples.Count} | Selected: {menu.SelectedExample + 1}",
            Color.LightBlue, 12f, _currentView);
    }

    private void CreateExampleList(Entity listContainer, ViewState.Menu menu)
    {
        if (_world == null || _exampleRunner == null) return;
        if (!IsValidParent(listContainer)) return;

        var contentContainer = UIFactory.CreateVStackLayout(_world, Vector2.Zero, 5f, listContainer);
        if (!IsValidParent(contentContainer)) return;

        for (int i = 0; i < _exampleRunner.Examples.Count; i++)
        {
            var example = _exampleRunner.Examples[i];
            var isSelected = i == menu.SelectedExample;

            // Validate parent before creating children
            if (!IsValidParent(contentContainer)) break;

            var itemContainer = UIFactory.CreateVStackLayout(_world, Vector2.Zero, 2f, contentContainer);
            if (!IsValidParent(itemContainer)) continue;

            var prefix = isSelected ? "► " : "  ";
            var titleColor = isSelected ? Color.Yellow : Color.White;

            if (isSelected)
            {
                UIFactory.CreatePanel(_world, Vector2.Zero, new Vector2(740, 50), itemContainer);
            }

            UIFactory.CreateText(_world, Vector2.Zero, $"{prefix}{i + 1:D2}. {example.Name}",
                titleColor, 16f, itemContainer);

            UIFactory.CreateText(_world, Vector2.Zero, $"    {example.Description}",
                Color.LightGray, 12f, itemContainer);
        }
    }

    private void CreateOutputView()
    {
        if (_world == null || _exampleRunner == null || !_state.IsOutput()) return;
        if (!IsValidParent(_rootContainer)) return;

        var output = _state.AsOutput();
        var selectedExample = _exampleRunner.Examples[output.SelectedExample];

        _currentView = UIFactory.CreateVStackLayout(_world, new Vector2(20, 20), 10f, _rootContainer);
        if (!IsValidParent(_currentView)) return;

        // Title
        UIFactory.CreateText(_world, Vector2.Zero, $"Result: {selectedExample.Name}",
            Color.Cyan, 20f, _currentView);

        // Controls
        UIFactory.CreateText(_world, Vector2.Zero, "ESC=Menu, R=Re-run, Mouse Wheel=Scroll",
            Color.Yellow, 14f, _currentView);

        // Output content
        UIFactory.CreateTextArea(_world, Vector2.Zero, new Vector2(760, 500),
            output.Content, _currentView);
    }

    private void OnRender(double deltaTime)
    {
        if (_world == null || _renderPipeline == null || _systemStage == null)
            return;

        try
        {
            _systemStage.Tick();

            _renderPipeline.BeginFrame((float)deltaTime);
            _renderPipeline.EndFrame();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ExampleViewer] Render exception: {ex.Message}");
        }
    }

    private void OnResize(Vector2D<int> newSize)
    {
        _renderPipeline?.Resize(newSize.X, newSize.Y);

        if (IsValidParent(_rootContainer))
        {
            new UIElement.View(_rootContainer).Size = new Vector2(newSize.X, newSize.Y);
        }

        // Rebuild UI safely
        if (_state.IsMenu())
            ShowMenu();
        else
            ShowOutput();
    }

    private void OnClosing()
    {
        Cleanup();
    }

    private void Cleanup()
    {
        if (_disposed) return;

        try
        {
            _currentView?.Destroy();
            _rootContainer?.Destroy();
            _currentView = null;
            _rootContainer = null;

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