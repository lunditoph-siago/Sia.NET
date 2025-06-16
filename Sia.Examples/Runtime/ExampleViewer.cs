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

public abstract record ViewState
{
    public sealed record Menu(
        int SelectedExample = 0,
        int HoveredExample = -1,
        float ScrollOffset = 0f) : ViewState
    {
        public Menu WithSelection(int selected) => this with { SelectedExample = selected };
        public Menu WithHover(int hovered) => this with { HoveredExample = hovered };
        public Menu WithScroll(float scrollDelta) => this with
        {
            ScrollOffset = Math.Max(0, ScrollOffset + scrollDelta)
        };
    }

    public sealed record Output(
        int SelectedExample,
        string Content,
        float ScrollOffset = 0f) : ViewState
    {
        public Output WithContent(string newContent) => this with { Content = newContent };
        public Output WithScroll(float scrollDelta) => this with
        {
            ScrollOffset = Math.Max(0, ScrollOffset + scrollDelta)
        };
    }

    public static ViewState InitialMenu() => new Menu();

    public static ViewState MenuFromOutput(Output output) => new Menu(output.SelectedExample);

    public static ViewState OutputFromMenu(Menu menu, string content) => new Output(menu.SelectedExample, content);
}

public static class ViewStateExtensions
{
    public static bool IsMenu(this ViewState state) => state is ViewState.Menu;
    public static bool IsOutput(this ViewState state) => state is ViewState.Output;

    public static ViewState.Menu AsMenu(this ViewState state) => (ViewState.Menu)state;
    public static ViewState.Output AsOutput(this ViewState state) => (ViewState.Output)state;

    public static ViewState ToMenu(this ViewState state) => state switch
    {
        ViewState.Menu menu => menu,
        ViewState.Output output => ViewState.MenuFromOutput(output),
        _ => throw new InvalidOperationException($"Unknown state type: {state.GetType()}")
    };

    public static ViewState ToOutput(this ViewState state, string content) => state switch
    {
        ViewState.Menu menu => ViewState.OutputFromMenu(menu, content),
        ViewState.Output output => output.WithContent(content),
        _ => throw new InvalidOperationException($"Unknown state type: {state.GetType()}")
    };
}

public sealed class ExampleViewer(IReadOnlyList<ExampleItem> examples) : IDisposable
{
    private IWindow? _window;
    private GL? _gl;
    private World? _world;
    private SystemStage? _systemStage;
    private RenderPipeline? _renderPipeline;
    private InputSystem? _inputAddon;
    private ExampleRunner? _exampleRunner;
    private IInputContext? _inputContext;
    private bool _disposed;

    // Application state
    private ViewState _state = ViewState.InitialMenu();

    // UI Entities
    private Entity? _rootContainer;
    private Entity? _currentView;

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
        if (_world is null) return;
        _rootContainer = UIFactory.CreatePanel(_world, Vector2.Zero, new(800, 600));
    }

    private void RegisterInputHandlers()
    {
        if (_inputContext is null) return;

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
                _state = menu.WithSelection(examples.Count - 1);
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
            case Key.Escape or Key.Backspace:
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
            examples.Count - 1);
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
            $"Total: {examples.Count} | Selected: {menu.SelectedExample + 1}",
            Color.LightBlue, 12f, _currentView);
    }

    private void CreateExampleList(Entity listContainer, ViewState.Menu menu)
    {
        if (_world == null || _exampleRunner == null) return;
        if (!IsValidParent(listContainer)) return;

        var contentContainer = UIFactory.CreateVStackLayout(_world, Vector2.Zero, 5f, listContainer);
        if (!IsValidParent(contentContainer)) return;

        for (int i = 0; i < examples.Count; i++)
        {
            var example = examples[i];
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
        var selectedExample = examples[output.SelectedExample];

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

    public void Run()
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
        _window.Run();
    }

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