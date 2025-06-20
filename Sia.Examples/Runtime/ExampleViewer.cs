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

public sealed class ExampleViewer(IReadOnlyList<ExampleItem> examples) : IDisposable
{
    private readonly IReadOnlyList<ExampleItem> _examples = examples;
    private readonly ExampleRunner _runner = new(examples);

    private IWindow? _window;
    private World? _world;
    private SystemStage? _systemStage;
    private RenderPipeline? _renderPipeline;

    private Entity? _outputTextEntity;
    private bool _disposed;

    public void Run()
    {
        var options = WindowOptions.Default with
        {
            Size = new Vector2D<int>(1000, 700),
            Title = "Sia.NET Example Viewer"
        };

        _window = Window.Create(options);
        _window.Load += OnLoad;
        _window.Render += OnRender;
        _window.Resize += OnResize;
        _window.Closing += OnClosing;
        _window.Run();
    }

    private void OnLoad()
    {
        var gl = GL.GetApi(_window!);
        var inputContext = _window!.CreateInput();

        _world = new World();
        Context<World>.Current = _world;
        Context<ExampleViewer>.Current = this;

        _renderPipeline = _world.AcquireAddon<RenderPipeline>();
        _renderPipeline.Initialize(gl, _window!.Size.X, _window!.Size.Y);

        var inputSystem = _world.AcquireAddon<InputSystem>();
        inputSystem.Initialize(inputContext);

        _world.AcquireAddon<Hierarchy<UIHierarchyTag>>();
        _world.AcquireAddon<UILayoutReactor>();

        _systemStage = SystemChain.Empty
            .Add<KeyboardStateSystem>()
            .Add<MouseStateSystem>()
            .Add<MouseButtonStateSystem>()
            .Add<MouseScrollStateSystem>()
            .Add<UILayoutComputeSystem>()
            .Add<UIClickHitTestSystem>()
            .Add<UIHoverStateSystem>()
            .Add<UIButtonInteractionSystem>()
            .Add<UIScrollInteractionSystem>()
            .Add<ExampleButtonSystem>()
            .CreateStage(_world);

        CreateUI();
    }

    private void OnRender(double dt)
    {
        _systemStage?.Tick();
        _renderPipeline?.BeginFrame((float)dt);
        _renderPipeline?.EndFrame();
    }

    private void OnResize(Vector2D<int> newSize)
    {
        _renderPipeline?.Resize(newSize.X, newSize.Y);
    }

    private void OnClosing()
    {
        if (!_disposed)
        {
            Dispose();
        }
    }

    private void CreateUI()
    {
        var windowSize = new Vector2(_window!.Size.X, _window.Size.Y);

        var root = _world!.Create(HList.From(
            new UIElement(Vector2.Zero, windowSize, true, true),
            new UIStyle(),
            new UILayer(0),
            new UIState(),
            new UIEventListener(),
            new UILayout(LayoutType.None, SizeValue.Pixels(windowSize.X), SizeValue.Pixels(windowSize.Y), Vector4.Zero, Vector4.Zero, true),
            new UIComputedLayout(),
            new Node<UIHierarchyTag>()
        ));

        CreateExampleButtons(root);
        CreateOutputArea(root);
    }

    private void CreateExampleButtons(Entity parent)
    {
        var listPanel = _world!.Create(HList.From(
            new UIElement(new Vector2(10, 10), new Vector2(300, _window!.Size.Y - 20), true, true),
            new UIStyle(Color.FromArgb(60, 40, 40, 40), Color.Gray, 1f, 4f),
            new UILayer(1),
            new UIState(),
            new UIEventListener(true, UIEventMask.Scroll),
            new UILayout(LayoutType.None, SizeValue.Pixels(300), SizeValue.Pixels(_window.Size.Y - 20), Vector4.Zero, new Vector4(10, 10, 10, 10), true),
            new UIComputedLayout(),
            new UIScrollable(new Vector2(300, _examples.Count * 50), Vector2.Zero, ScrollDirection.Vertical, 20f),
            new Node<UIHierarchyTag>(parent)
        ));

        for (int i = 0; i < _examples.Count; i++)
        {
            var example = _examples[i];
            var yPos = i * 50 + 10;

            var button = _world!.Create(HList.From(
                new UIElement(new Vector2(10, yPos), new Vector2(280, 40), true, true),
                new UIButton(),
                new UIStyle(Color.LightGray, Color.Gray, 1f, 4f),
                new UILayer(2),
                new UIState(),
                new UIEventListener(true, UIEventMask.Click | UIEventMask.Hover),
                new UILayout(LayoutType.None, SizeValue.Pixels(280), SizeValue.Pixels(40), Vector4.Zero, new Vector4(8, 8, 8, 8), true),
                new UIComputedLayout(),
                new ExampleIndex(i),
                new Node<UIHierarchyTag>(listPanel)
            ));

            var buttonText = _world!.Create(HList.From(
                new UIElement(new Vector2(0, 0), new Vector2(280 - 16, 60), true, false),
                new UIText($"{i + 1:D2}. {example.Name}", Color.Black, 14f, TextAlignment.Left),
                new UIStyle(),
                new UILayer(3),
                new UIState(),
                new UIEventListener(false, UIEventMask.None),
                new UILayout(LayoutType.None, SizeValue.Auto, SizeValue.Auto, Vector4.Zero, Vector4.Zero, true),
                new UIComputedLayout(),
                new Node<UIHierarchyTag>(button)
            ));
        }
    }

    private void CreateOutputArea(Entity parent)
    {
        var outputPanel = _world!.Create(HList.From(
            new UIElement(new Vector2(320, 10), new Vector2(_window!.Size.X - 330, _window.Size.Y - 20), true, true),
            new UIStyle(Color.FromArgb(80, 30, 30, 30), Color.DimGray, 1f, 4f),
            new UILayer(1),
            new UIState(),
            new UIEventListener(true, UIEventMask.Scroll),
            new UILayout(LayoutType.None, SizeValue.Pixels(_window.Size.X - 330), SizeValue.Pixels(_window.Size.Y - 20), Vector4.Zero, new Vector4(10, 10, 10, 10), true),
            new UIComputedLayout(),
            new UIScrollable(new Vector2(_window.Size.X - 350, 1000), Vector2.Zero, ScrollDirection.Vertical, 25f),
            new Node<UIHierarchyTag>(parent)
        ));

        _outputTextEntity = _world!.Create(HList.From(
            new UIElement(new Vector2(10, 10), new Vector2(outputPanel.Get<UIElement>().Size.X - 20, 0), true, false),
            new UIText("Please select an example from the left list to run.", Color.White, 13f, TextAlignment.Left),
            new UIStyle(),
            new UILayer(2),
            new UIState(),
            new UIEventListener(false, UIEventMask.None),
            new UILayout(LayoutType.None, SizeValue.Auto, SizeValue.Auto, Vector4.Zero, Vector4.Zero, true),
            new UIComputedLayout(),
            new OutputTextTag(),
            new Node<UIHierarchyTag>(outputPanel)
        ));
    }

    public void DisplayOutput(string content)
    {
        if (_outputTextEntity is null || !_outputTextEntity.IsValid) return;

        _outputTextEntity.Execute(new UIText.SetText(content));

        var lines = content.Split('\n').Length;
        var textHeight = lines * 16f;
        new UIElement.View(_outputTextEntity).Size = new Vector2(
            _outputTextEntity.Get<UIElement>().Size.X,
            textHeight);
    }

    public string RunExample(int index) => _runner.RunExample(index);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            _runner.Dispose();
            _systemStage?.Dispose();
            _world?.Dispose();
        }
        catch (ObjectDisposedException) { }
    }

    public partial record struct ExampleIndex([Sia] int Value);
    public readonly record struct OutputTextTag;

    private sealed class ExampleButtonSystem : EventSystemBase
    {
        private ExampleViewer? _viewer;

        public override void Initialize(World world)
        {
            base.Initialize(world);
            RecordEvents<UIEvents>();
            _viewer = Context<ExampleViewer>.Current;
        }

        protected override void HandleEvent<TEvent>(Entity entity, in TEvent @event)
        {
            if (@event is not UIEvents.ButtonPressed) return;
            if (!entity.Contains<ExampleIndex>()) return;

            var exampleIndex = entity.Get<ExampleIndex>().Value;
            var output = _viewer?.RunExample(exampleIndex);

            if (output is not null)
            {
                _viewer!.DisplayOutput(output);
            }
        }
    }
}