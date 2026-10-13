#if BROWSER
using System.Reflection;
using System.Text;
using System.Text.Json;
using Sia;
using Sia.Graphics.Reactive;
using Sia.Input;
using Sia.Reactive;
using Sia.RenderGraph;
using Sia.WebGPU;
using Sia.Window;
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
    private readonly Entity _window;
    private WgpuHandle<WGPUInstance> _instance;
    private WgpuHandle<WGPUAdapter> _adapter;
    private WgpuHandle<WGPUDevice> _device;
    private WgpuHandle<WGPUQueue> _queue;
    private WgpuHandle<WGPUSurface> _surface;
    private WgpuRenderGraphRegistry? _graph;
    private ReactiveMount<EditorPipelineFrame> _mount;
    private int _maxTextureDimension;
    private double _previousTime, _startTime;
    private bool _disposed, _configured;
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private JSObject? _canvas;
    private byte[]? _selectorUtf8;
    private static int _canvasNumber;
    private static readonly RenderGraphTextureKey Output = new("editor.canvas");

    private EditorPipelineSession(Assembly assembly, IReadOnlyDictionary<int, ShaderArtifact> shaders,
        ReactiveComponent<EditorPipelineFrame> render, bool spirvRoute, Action<string> status)
    {
        _assembly = assembly;
        _shaders = shaders;
        _render = render;
        _spirvRoute = spirvRoute;
        _status = status;
        _window = _world.Create();
        _window.Add(new WindowState(default, default, false));
    }

    public static async Task<EditorPipelineSession> CreateAsync(Assembly assembly,
        IReadOnlyDictionary<int, ShaderArtifact> shaders, ReactiveComponent<EditorPipelineFrame> render,
        bool spirvRoute, Action<string> status, CancellationToken cancellationToken)
    {
        var session = new EditorPipelineSession(assembly, shaders, render, spirvRoute, status);
        try {
            await session.InitializeAsync(cancellationToken);
            ObjectDisposedException.ThrowIf(session._disposed, session);
            return session;
        }
        catch { session.Dispose(); throw; }
    }

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        _status("Creating programmable pipeline…");
        var renderThread = CanvasThread();
        var canvasId = "editor-pipeline-canvas-" + Interlocked.Increment(ref _canvasNumber);
        var selector = "#" + canvasId;
        _selectorUtf8 = Encoding.UTF8.GetBytes(selector + "\0");
        _canvas = CreateCanvas("editor-pipeline-preview", canvasId, checked((int)renderThread));
        await WaitCanvas(_canvas).WaitAsync(cancellationToken);
        _instance = Wgpu.CreateInstance();
        _surface = Wgpu.CreateCanvasSurface(_instance, selector);
        _adapter = await Wgpu.RequestAdapterAsync(_instance, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        _device = await Wgpu.RequestDeviceAsync(_adapter, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        _queue = Wgpu.GetQueue(_device);
        _maxTextureDimension = checked((int)Wgpu.GetLimits(_device).MaxTextureDimension2D);
        _graph = _world.ConfigureWgpuRenderGraph(_device, _queue);
        ObserveGpuDevice();
        _ = RunFramesAsync();
        await _ready.Task.WaitAsync(cancellationToken);
        _status($"{(_spirvRoute ? "SPIR-V → WGSL" : "WGSL")} · programmable pipeline running");
    }

    private ReactiveNode Render(in EditorPipelineFrame frame, ref Hooks hooks)
    {
        hooks.UseImportedRenderGraphTexture(frame.Graph, Output,
            new("Canvas", RenderGraphTextureFormat.BGRA8Unorm, (uint)frame.Width, (uint)frame.Height,
                usage: RenderGraphTextureUsage.RenderAttachment));
        return _render(in frame, ref hooks);
    }

    private void Frame(double milliseconds, double cssWidth, double cssHeight, double scale, ReadOnlySpan<double> inputs)
    {
        if (_disposed || _graph is null) return;
        CheckGpuError();
        var width = Math.Clamp((int)Math.Round(cssWidth * scale), 1, _maxTextureDimension);
        var height = Math.Clamp((int)Math.Round(cssHeight * scale), 1, _maxTextureDimension);
        Resize(width, height, cssWidth, cssHeight, scale);
        var surfaceTexture = Wgpu.AcquireSurfaceTexture(_surface);
        if (!surfaceTexture.HasTexture) throw new InvalidOperationException($"Canvas acquisition failed: {surfaceTexture.Status}");
        try {
            if (_startTime == 0) _startTime = milliseconds;
            var frame = new EditorPipelineFrame(_world, _window, _graph, _device, _queue,
                Output, WGPUTextureFormat.BGRA8Unorm, width, height,
                (milliseconds - _startTime) / 1000, _previousTime == 0 ? 0 : (milliseconds - _previousTime) / 1000) {
                Assembly = _assembly, Shaders = _shaders, SpirvRoute = _spirvRoute,
            };
            _previousTime = milliseconds;
            if (_mount.IsMounted) _mount.Update(in frame);
            else _mount = _world.Mount<EditorPipelineFrame>(Render, in frame);
            for (var i = 0; i + 4 < inputs.Length; i += 5)
                Input((int)inputs[i], inputs[i + 1], inputs[i + 2], (int)inputs[i + 3], (int)inputs[i + 4]);
            _world.FlushReactive();
            using var binding = _graph.BindImportedTexture(Output, surfaceTexture.Texture);
            _world.ExecuteWgpuRenderGraph();
            Wgpu.PresentSurfaceOrThrow(_surface);
            ValidateFirstFrame();
        }
        finally { Wgpu.Release(ref surfaceTexture); }
    }

    private unsafe void Resize(int width, int height, double cssWidth, double cssHeight, double scale)
    {
        var previous = _window.Get<WindowState>();
        var next = new WindowState(new((int)cssWidth, (int)cssHeight), new(width, height), false,
            new((float)scale, (float)scale));
        _window.Get<WindowState>() = next;
        if (previous.Size != next.Size) _world.Send(_window, new WindowEvents.Resized(next.Size));
        if (previous.FramebufferSize != next.FramebufferSize) _world.Send(_window, new WindowEvents.FramebufferResized(next.FramebufferSize));
        if (previous.ContentScale != next.ContentScale) _world.Send(_window, new WindowEvents.ContentScaleChanged(next.ContentScale));
        if (_configured && previous.FramebufferSize == next.FramebufferSize) return;
        fixed (byte* selector = _selectorUtf8) {
            var result = ResizeCanvas(selector, width, height);
            if (result != 0) throw new InvalidOperationException($"Canvas resize failed: {result}");
        }
        Wgpu.ConfigureSurface(_surface, new WGPUSurfaceConfiguration {
            Device = (WGPUDevice*)_device.Handle, Format = WGPUTextureFormat.BGRA8Unorm,
            Usage = WGPUTextureUsage.RenderAttachment, Width = (uint)width, Height = (uint)height,
            AlphaMode = WGPUCompositeAlphaMode.Opaque, PresentMode = WGPUPresentMode.Fifo,
        });
        _configured = true;
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
                    if (_configured) Wgpu.UnconfigureSurface(_surface);
                    if (canvas is not null) ReleaseCanvasMapping();
                    if (!_device.IsNull) Wgpu.DestroyDevice(_device);
                }
                finally {
                    Wgpu.Release(ref _queue);
                    Wgpu.Release(ref _device);
                    Wgpu.Release(ref _adapter);
                    Wgpu.Release(ref _surface);
                    Wgpu.Release(ref _instance);
                    canvas?.Dispose();
                }
            }
        }
    }

    private unsafe void ValidateFirstFrame()
    {
        if (_ready.Task.IsCompleted) return;
        fixed (byte* selector = _selectorUtf8) {
            if (_graph!.ExecutionCount == 1) ValidateFrame(selector);
            if (FrameValidated(selector) != 0) _ready.TrySetResult();
        }
    }
    [DllImport("__Internal_emscripten", EntryPoint = "siaEditorValidateFrame")]
    private static extern unsafe void ValidateFrame(byte* selector);
    [DllImport("__Internal_emscripten", EntryPoint = "siaEditorFrameValidated")]
    private static extern unsafe int FrameValidated(byte* selector);
    private unsafe void ObserveGpuDevice()
    {
        fixed (byte* selector = _selectorUtf8) ObserveDevice(selector, _device.Handle);
    }
    private unsafe void CheckGpuError()
    {
        nint error;
        fixed (byte* selector = _selectorUtf8) error = DeviceError(selector);
        if (error == 0) return;
        try { throw new InvalidOperationException(Marshal.PtrToStringUTF8(error)); }
        finally { Free(error); }
    }
    [DllImport("__Internal_emscripten", EntryPoint = "siaEditorObserveDevice")]
    private static extern unsafe void ObserveDevice(byte* selector, nint device);
    [DllImport("__Internal_emscripten", EntryPoint = "siaEditorDeviceError")]
    private static extern unsafe nint DeviceError(byte* selector);
    [DllImport("__Internal_emscripten", EntryPoint = "siaEditorFree")]
    private static extern void Free(nint pointer);
    private async Task RunFramesAsync()
    {
        try {
            while (!_disposed) {
                var packet = await NextFrame(_canvas!);
                // Leave the JavaScript Promise callback before native work or DOM imports.
                await Task.Yield();
                if (_disposed) return;
                var frame = JsonSerializer.Deserialize<double[]>(packet)!;
                Frame(frame[0], frame[1], frame[2], frame[3], frame.AsSpan(4));
            }
        }
        catch (Exception error) {
            if (_disposed) return;
            _ready.TrySetException(error);
            try { _status("Pipeline stopped: " + error.Message); }
            finally { Dispose(); }
        }
    }
    private unsafe void ReleaseCanvasMapping()
    {
        fixed (byte* selector = _selectorUtf8) ReleaseCanvas(selector);
    }
    [DllImport("__Internal_emscripten", EntryPoint = "siaEditorCanvasThread")]
    private static extern nuint CanvasThread();
    [DllImport("__Internal_emscripten", EntryPoint = "emscripten_set_canvas_element_size")]
    private static extern unsafe int ResizeCanvas(byte* selector, int width, int height);
    [DllImport("__Internal_emscripten", EntryPoint = "siaEditorReleaseCanvas")]
    private static extern unsafe void ReleaseCanvas(byte* selector);
    [JSImport("createPipelineCanvas", "main.js")]
    private static partial JSObject CreateCanvas(string containerId, string canvasId, int thread);
    [JSImport("waitPipelineCanvas", "main.js")]
    [return: JSMarshalAs<JSType.Promise<JSType.Void>>]
    private static partial Task WaitCanvas(JSObject canvas);
    [JSImport("nextPipelineFrame", "main.js")]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    private static partial Task<string> NextFrame(JSObject canvas);
    [JSImport("disposePipelineCanvas", "main.js")]
    private static partial void StopCanvas(JSObject canvas);
}
#endif
