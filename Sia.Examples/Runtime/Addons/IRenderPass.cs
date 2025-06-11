using Silk.NET.OpenGL;

namespace Sia.Examples.Runtime.Addons;

public interface IRenderPass : IDisposable
{
    /// <summary>
    /// Render pass name, for debugging and identification
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Render priority, the smaller the number, the sooner it is executed
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// Whether to enable this render pass
    /// </summary>
    bool IsEnabled { get; set; }

    /// <summary>
    /// Initialize render pass, pass in OpenGL context
    /// </summary>
    /// <param name="gl">OpenGL context</param>
    void Initialize(GL gl);

    /// <summary>
    /// Execute render pass logic
    /// </summary>
    /// <param name="world">World instance</param>
    /// <param name="pipeline">Render pipeline instance</param>
    void Execute(World world, RenderPipeline pipeline);

    /// <summary>
    /// Handle window resize
    /// </summary>
    /// <param name="width">New width</param>
    /// <param name="height">New height</param>
    void OnResize(int width, int height);
}