using System.Reflection;
using Sia;
using Sia.Graphics.Reactive;
using Sia.RenderGraph;
using Sia.WebGPU;

namespace Sia_Examples.Editor;

/// <summary>Borrowed resources for an editor-mounted ReactiveComponent. GPU handles belong to the session.</summary>
public readonly record struct EditorPipelineFrame(
    World World, Entity Window, WgpuRenderGraphRegistry Graph,
    WgpuHandle<WGPUDevice> Device, WgpuHandle<WGPUQueue> Queue,
    RenderGraphTextureKey Output, WGPUTextureFormat Format,
    int Width, int Height, double Elapsed, double Delta)
{
    internal Assembly? Assembly { get; init; }
    internal IReadOnlyDictionary<int, ShaderArtifact>? Shaders { get; init; }
    internal bool SpirvRoute { get; init; }

    /// <summary>Gets the compiled entry from this run's immutable C# image; never executes shader IL.</summary>
    public (string Source, string Entry) Shader(Delegate entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.Method.Module.Assembly != Assembly || Shaders is null
            || !Shaders.TryGetValue(entry.Method.MetadataToken, out var shader)) {
            throw new ArgumentException("Use a shader delegate declared in the current pipeline source.", nameof(entry));
        }
        return (SpirvRoute ? shader.TranslatedWgsl : shader.Wgsl, shader.EntryName);
    }
}
