#if BROWSER
using System.Reflection;
using System.Text.Json;
using Sia;
using Sia.Graphics;
using Sia.Graphics.Reactive;
using Sia.Input;
using Sia.Reactive;
using Sia.RenderGraph;
using Sia.WebGPU;
using Sia.Window;
using Sia_Examples.Notebook;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.InteropServices;

namespace Sia_Examples.Editor;

internal sealed partial class EditorPipelineSession : IDisposable
{
    private readonly World _world = new();
    private readonly Assembly _assembly;
    private readonly IReadOnlyDictionary<int, ShaderArtifact> _shaders;
    private readonly ReactiveComponent<EditorPipelineFrame> _render;
    private readonly Action<string> _status;
    private readonly bool _spirvRoute;
    private readonly bool _normalCanvas;
    private readonly Entity _window;
    private WgpuHandle<WGPUInstance> _instance;
    private WgpuHandle<WGPUAdapter> _adapter;
    private WgpuHandle<WGPUDevice> _device;
    private WgpuHandle<WGPUQueue> _queue;
    private WgpuRenderTarget? _target;
    private WgpuRenderGraphRegistry? _graph;
    private ReactiveMount<EditorPipelineFrame> _mount;
    private int _maxTextureDimension;
    private double _previousTime, _startTime;
    private bool _disposed;
    public bool Paused { get; set; }
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private JSObject? _canvas;
    private static int _canvasNumber;
    private static readonly RenderGraphTextureKey Output = new("editor.canvas");
    private readonly List<double> _frameInputs = [];
    private readonly EditorUniformSession _uniforms;
    private readonly ProgramConsole _console;

    private EditorPipelineSession(Assembly assembly, IReadOnlyDictionary<int, ShaderArtifact> shaders,
        ReactiveComponent<EditorPipelineFrame> render, bool spirvRoute, bool normalCanvas, Action<string> status,
        EditorUniformInspector inspector, ProgramConsole console)
    {
        _assembly = assembly;
        _shaders = shaders;
        _render = render;
        _spirvRoute = spirvRoute;
        _normalCanvas = normalCanvas;
        _status = status;
        _uniforms = new(inspector);
        _console = console;
        _window = _world.Create();
        _window.Add(new WindowState(default, default, false));
    }

    public static async Task<EditorPipelineSession> CreateAsync(Assembly assembly,
        IReadOnlyDictionary<int, ShaderArtifact> shaders, ReactiveComponent<EditorPipelineFrame> render,
        bool spirvRoute, bool normalCanvas, Action<string> status, EditorUniformInspector inspector, ProgramConsole console, CancellationToken cancellationToken)
    {
        var session = new EditorPipelineSession(assembly, shaders, render, spirvRoute, normalCanvas, status, inspector, console);
        try {
            await session.InitializeAsync(cancellationToken);
            ObjectDisposedException.ThrowIf(session._disposed, session);
            return session;
        }
        catch { session.Dispose(); throw; }
    }

    public void ActivateUniforms() => _uniforms.Activate();

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        _status("Creating programmable pipeline…");
        var canvasId = "editor-pipeline-canvas-" + Interlocked.Increment(ref _canvasNumber);
        _canvas = CreateCanvas("editor-pipeline-preview", canvasId);
        await NextFrame(_canvas).WaitAsync(cancellationToken);
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        _instance = Wgpu.CreateInstance();
        _adapter = await AwaitGpuAsync(Wgpu.RequestAdapterAsync(_instance, cancellationToken), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        _device = await AwaitGpuAsync(Wgpu.RequestDeviceAsync(_adapter, cancellationToken), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        _queue = Wgpu.GetQueue(_device);
        var limits = Wgpu.GetLimits(_device);
        _maxTextureDimension = checked((int)limits.MaxTextureDimension2D);
        _graph = _world.ConfigureWgpuRenderGraph(_device, _queue);
        _status("Connecting canvas…");
        _target = await WgpuRenderTarget.FromCanvasAsync(_instance, _device, _queue, canvasId,
            _normalCanvas ? WgpuCanvasPresentation.Readback : WgpuCanvasPresentation.Direct, cancellationToken);
        _status("Rendering first frame…");
        _ = RunFramesAsync();
        await _ready.Task.WaitAsync(cancellationToken);
        _status($"{(_spirvRoute ? "SPIR-V → WGSL" : "WGSL")} · {(_normalCanvas ? "Normal canvas" : "Offscreen canvas")} · programmable pipeline running");
    }

    private async Task<T> AwaitGpuAsync<T>(Task<T> request, CancellationToken cancellationToken)
    {
        do {
            await NextFrame(_canvas!).WaitAsync(cancellationToken);
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
        } while (!request.IsCompleted);
        return request.GetAwaiter().GetResult();
    }

    private ReactiveNode Render(in EditorPipelineFrame frame, ref Hooks hooks)
    {
        hooks.UseImportedRenderGraphTexture(frame.Graph, Output,
            new("Canvas", frame.Format == WGPUTextureFormat.RGBA8Unorm ? RenderGraphTextureFormat.RGBA8Unorm : RenderGraphTextureFormat.BGRA8Unorm,
                (uint)frame.Width, (uint)frame.Height,
                usage: RenderGraphTextureUsage.RenderAttachment));
        return _render(in frame, ref hooks);
    }

    private unsafe bool Frame(double milliseconds, double cssWidth, double cssHeight, double scale, ReadOnlySpan<double> inputs)
    {
        if (_disposed || _graph is null) return false;
        var width = Math.Clamp((int)Math.Round(cssWidth * scale), 1, _maxTextureDimension);
        var height = Math.Clamp((int)Math.Round(cssHeight * scale), 1, _maxTextureDimension);
        if (!_target!.TryAcquireFrame(width, height, out var output)) return false;
        using (output) {
            var validate = _firstFrameValidation is null;
            if (validate) WgpuUnsafe.wgpuDevicePushErrorScope((WGPUDevice*)_device.Handle, WGPUErrorFilter.Validation);
            try {
                UpdateWindow(width, height, cssWidth, cssHeight, scale);
                if (_startTime == 0) _startTime = milliseconds;
                var frame = new EditorPipelineFrame(_world, _window, _graph, _device, _queue,
                    Output, output.Format, output.Width, output.Height,
                    (milliseconds - _startTime) / 1000, _previousTime == 0 ? 0 : (milliseconds - _previousTime) / 1000) {
                    Assembly = _assembly, Shaders = _shaders, SpirvRoute = _spirvRoute, Uniforms = _uniforms,
                };
                _previousTime = milliseconds;
                if (_mount.IsMounted) _mount.Update(in frame);
                else _mount = _world.Mount<EditorPipelineFrame>(Render, in frame);
                for (var i = 0; i + 4 < inputs.Length; i += 5)
                    Input((int)inputs[i], inputs[i + 1], inputs[i + 2], (int)inputs[i + 3], (int)inputs[i + 4]);
                _world.FlushReactive();
                _uniforms.Sync();
                using var binding = _graph.BindImportedTexture(Output, output.Texture);
                _world.ExecuteWgpuRenderGraph();
                output.Present();
            }
            finally { if (validate) _firstFrameValidation = EndFirstFrameValidation(); }
        }
        return true;
    }

    private void UpdateWindow(int width, int height, double cssWidth, double cssHeight, double scale)
    {
        var previous = _window.Get<WindowState>();
        var next = new WindowState(new((int)cssWidth, (int)cssHeight), new(width, height), false,
            new((float)scale, (float)scale));
        _window.Get<WindowState>() = next;
        if (previous.Size != next.Size) _world.Send(_window, new WindowEvents.Resized(next.Size));
        if (previous.FramebufferSize != next.FramebufferSize) _world.Send(_window, new WindowEvents.FramebufferResized(next.FramebufferSize));
        if (previous.ContentScale != next.ContentScale) _world.Send(_window, new WindowEvents.ContentScaleChanged(next.ContentScale));
    }

    private void Input(int kind, double x, double y, int code, int modifiers)
    {
        var mods = (KeyModifiers)modifiers;
        switch (kind) {
            case 0: _world.Send(_window, new InputEvents.MouseMoved(new(x, y))); break;
            case 1: _world.Send(_window, new InputEvents.MouseScrolled(new(x, y))); break;
            case 2: _world.Send(_window, new InputEvents.MouseEntered()); break;
            case 3: _world.Send(_window, new InputEvents.MouseExited()); break;
            case 4: _world.Send(_window, new InputEvents.MouseButtonPressed((MouseButton)code, mods)); break;
            case 5: _world.Send(_window, new InputEvents.MouseButtonReleased((MouseButton)code, mods)); break;
            case 6: _world.Send(_window, new InputEvents.KeyPressed((Key)code, 0, mods)); break;
            case 7: _world.Send(_window, new InputEvents.KeyRepeated((Key)code, 0, mods)); break;
            case 8: _world.Send(_window, new InputEvents.KeyReleased((Key)code, 0, mods)); break;
            case 9: _world.Send(_window, new InputEvents.TextEntered((uint)code)); break;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        using var capture = _console.Capture();
        _ready.TrySetCanceled();
        var canvas = _canvas;
        _canvas = null;
        try {
            if (canvas is not null) StopCanvas(canvas);
            _window.Get<WindowState>() = _window.Get<WindowState>() with { CloseRequested = true };
            _world.Send(_window, new WindowEvents.CloseRequested());
            if (_mount.IsMounted) _mount.Unmount();
            _world.FlushReactive();
        }
        finally {
            try { _world.Dispose(); }
            finally {
                try {
                    _uniforms.Dispose();
                    _target?.Dispose();
                    if (!_device.IsNull) Wgpu.DestroyDevice(_device);
                }
                finally {
                    Wgpu.Release(ref _queue);
                    Wgpu.Release(ref _device);
                    Wgpu.Release(ref _adapter);
                    Wgpu.Release(ref _instance);
                    canvas?.Dispose();
                    _console.Dispose();
                }
            }
        }
    }

    private async Task RunFramesAsync()
    {
        try {
            while (!_disposed) {
                var packet = await NextFrame(_canvas!);
                // Leave the JavaScript Promise callback before native work or DOM imports.
                await Task.Yield();
                if (_disposed) return;
                var frame = JsonSerializer.Deserialize<double[]>(packet)!;
                for (var i = 4; i < frame.Length; i++) _frameInputs.Add(frame[i]);
                if (Paused) continue;
                _uniforms.ApplyEdits();
                if (!Frame(frame[0], frame[1], frame[2], frame[3], CollectionsMarshal.AsSpan(_frameInputs))) continue;
                _frameInputs.Clear();
                if (_disposed) return;
                if (_firstFrameValidation is { IsCompleted: true } validation) {
                    validation.GetAwaiter().GetResult();
                    _ready.TrySetResult();
                }
            }
        }
        catch (Exception error) {
            if (_disposed) return;
            _ready.TrySetException(error);
            try { _status("Pipeline stopped: " + error.Message); }
            finally { Dispose(); }
        }
    }
    [JSImport("createPipelineCanvas", "main.js")]
    private static partial JSObject CreateCanvas(string containerId, string canvasId);
    [JSImport("nextPipelineFrame", "main.js")]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    private static partial Task<string> NextFrame(JSObject canvas);
    [JSImport("disposePipelineCanvas", "main.js")]
    private static partial void StopCanvas(JSObject canvas);
}
#endif
