using System.Collections.Concurrent;
using Sia.Examples.Runtime.Addons.Passes;
using Silk.NET.OpenGL;

namespace Sia.Examples.Runtime.Addons;

public class RenderPipeline : IAddon
{
    private GL? _gl;
    private readonly ConcurrentQueue<IRenderPass> _renderPasses = new();
    private readonly List<IRenderPass> _activePasses = [];
    private World? _world;

    public GL GL => _gl ?? throw new InvalidOperationException("RenderPipeline not initialized");
    public int WindowWidth { get; private set; }
    public int WindowHeight { get; private set; }
    public float DeltaTime { get; private set; }
    public float TotalTime { get; private set; }

    public readonly ref struct RenderStats(int activePasses, float deltaTime, float totalTime)
    {
        public readonly int ActivePasses = activePasses;
        public readonly float DeltaTime = deltaTime;
        public readonly float TotalTime = totalTime;
    }

    public void Initialize(GL gl, int windowWidth, int windowHeight)
    {
        _gl = gl;
        WindowWidth = windowWidth;
        WindowHeight = windowHeight;

        InitializeGraphicsState();
        RegisterPass(new UIRenderPass(WindowWidth, WindowHeight));
    }

    private void InitializeGraphicsState()
    {
        if (_gl is null) return;

        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        _gl.ClearColor(0.1f, 0.1f, 0.15f, 1.0f);
    }

    public void OnInitialize(World world) => _world = world;

    public void OnUninitialize(World world)
    {
        foreach (var pass in _activePasses)
            pass.Dispose();

        _activePasses.Clear();

        while (_renderPasses.TryDequeue(out var pendingPass))
            pendingPass.Dispose();

        _world = null;
    }

    public void RegisterPass(IRenderPass pass) => _renderPasses.Enqueue(pass);

    public void BeginFrame(float deltaTime)
    {
        DeltaTime = deltaTime;
        TotalTime += deltaTime;

        if (_gl is null || _world is null) return;

        ProcessNewRenderPasses();
        ClearFramebuffer();
        ExecutePasses(_world);
    }

    private void ProcessNewRenderPasses()
    {
        while (_renderPasses.TryDequeue(out var pass))
        {
            try
            {
                pass.Initialize(_gl!);
                _activePasses.Add(pass);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[RenderPipeline] Failed to initialize pass {pass.Name}: {ex.Message}");
                pass.Dispose();
            }
        }
    }

    private void ClearFramebuffer()
    {
        _gl?.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
    }

    private void ExecutePasses(World world)
    {
        if (_gl is null || _activePasses.Count == 0) return;

        // Sort passes by priority (lower numbers render first)
        _activePasses.Sort(static (a, b) => a.Priority.CompareTo(b.Priority));

        foreach (var pass in _activePasses.Where(static p => p.IsEnabled))
        {
            try
            {
                pass.Execute(world, this);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[RenderPipeline] Pass '{pass.Name}' execution failed: {ex.Message}");
            }
        }
    }

    public void EndFrame()
    {
        _gl?.Flush();
    }

    public void Resize(int width, int height)
    {
        WindowWidth = width;
        WindowHeight = height;

        _gl?.Viewport(0, 0, (uint)width, (uint)height);

        foreach (var pass in _activePasses)
        {
            try
            {
                pass.OnResize(width, height);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[RenderPipeline] Pass '{pass.Name}' resize failed: {ex.Message}");
            }
        }
    }

    public TPass? GetPass<TPass>() where TPass : class, IRenderPass =>
        _activePasses.OfType<TPass>().FirstOrDefault();

    public RenderStats GetStats() => new(_activePasses.Count, DeltaTime, TotalTime);

    public bool RemovePass<TPass>() where TPass : class, IRenderPass
    {
        if (GetPass<TPass>() is not { } pass) return false;

        _activePasses.Remove(pass);
        pass.Dispose();
        return true;
    }
}