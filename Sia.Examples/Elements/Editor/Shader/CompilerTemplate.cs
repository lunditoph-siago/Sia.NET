namespace Sia_Examples.Editor;

internal static class CompilerTemplate
{
    public const string Source = """"
        using System;
        using System.Runtime.InteropServices;
        using Sia;
        using Sia.Input;
        using Sia.Math;
        using Sia.Reactive;
        using Sia.RenderGraph;
        using Sia.Graphics.Reactive;
        using Sia.Spirv;
        using Sia.WebGPU;
        using Sia.Window;
        using Sia_Examples.Editor;

        public static class EditorProgram
        {
            private static readonly RenderGraphPassKey Clear = new("clear");
            private static readonly RenderGraphPassKey Mesh = new("mesh");
            private readonly record struct Controls(bool Mesh, float X, float Y);
            private readonly record struct InputCapture(Entity Window, State<Controls> State);
            private readonly record struct GpuDependencies(WgpuHandle<WGPUDevice> Device, WgpuHandle<WGPUQueue> Queue,
                WGPUTextureFormat Format, DrawResources Draw, string Vertex, string Fragment);
            private struct Material
            {
                [ColorAttribute] public float4 Tint;
                [Range(0, 2)] public float Exposure;
                public bool Animate;
            }

            public static ReactiveNode Render(in EditorPipelineFrame frame, ref Hooks hooks)
            {
                var state = hooks.UseState(new Controls(true, 0.5f, 0.5f));
                var material = hooks.UseUniforms(in frame, "Material", new Material {
                    Tint = new float4(1, 1, 1, 1), Exposure = 1, Animate = true,
                });
                var input = new InputCapture(frame.Window, state);
                hooks.UseEvent<InputEvents.MouseMoved, InputCapture>(input,
                    static (Entity target, in InputEvents.MouseMoved e, in InputCapture c) => {
                        if (target != c.Window) return;
                        var size = target.Get<WindowState>().Size;
                        c.State.Set(c.State.Value with {
                            X = math.clamp((float)e.Position.X / Math.Max(1, size.Width), 0f, 1f),
                            Y = math.clamp((float)e.Position.Y / Math.Max(1, size.Height), 0f, 1f)
                        });
                    });
                hooks.UseEvent<InputEvents.KeyPressed, InputCapture>(input,
                    static (Entity target, in InputEvents.KeyPressed e, in InputCapture c) => {
                        if (target == c.Window && e.Key == Key.Space)
                            c.State.Set(c.State.Value with { Mesh = !c.State.Value.Mesh });
                    });
                var draw = hooks.UseRef(static () => new DrawResources()).Value;
                draw.Frame = frame;
                draw.Controls = state.Value;
                draw.Material = material;
                hooks.UseRenderGraphPass(frame.Graph, Clear, "Animated clear", frame.Output, DeclareClear);
                hooks.UseWgpuRenderGraphPassHandler(frame.Graph, Clear, draw.Clear);
                return Reactive.When(state.Value.Mesh, Reactive.Component<EditorPipelineFrame>(Overlay, in frame));
            }

            private static void DeclareClear(in RenderGraphTextureKey output, RenderGraphPassDeclarationBuilder pass)
                => pass.Write(output, RenderGraphTextureUsage.RenderAttachment).MarkSideEffect();

            private static ReactiveNode Overlay(in EditorPipelineFrame frame, ref Hooks hooks)
            {
                var vertex = frame.Shader((Action)Vertex);
                var fragment = frame.Shader((Action)Color);
                var draw = hooks.UseRef(static () => new DrawResources()).Value;
                draw.Frame = frame;
                hooks.UseEffect(new GpuDependencies(frame.Device, frame.Queue, frame.Format, draw, vertex.Source, fragment.Source),
                    static (in GpuDependencies d) => { d.Draw.Create(d.Device, d.Queue, d.Format, d.Vertex, d.Fragment); return d.Draw; },
                    static (in DrawResources d) => d.Dispose());
                hooks.UseRenderGraphPass(frame.Graph, Mesh, "C# indexed mesh", frame.Output, DeclareMesh);
                hooks.UseWgpuRenderGraphPassHandler(frame.Graph, Mesh, draw.Mesh);
                return Reactive.None;
            }

            private static void DeclareMesh(in RenderGraphTextureKey output, RenderGraphPassDeclarationBuilder pass)
                => pass.ReadWrite(output, RenderGraphTextureUsage.RenderAttachment)
                    .DependsOn(Clear).MarkSideEffect();

            [SpirvVertexShader]
            public static void Vertex()
            {
                Gpu.SetPosition(Gpu.GetInput(0, 0), Gpu.GetInput(0, 1), Gpu.GetInput(0, 2), 1f);
                Gpu.SetOutput(0, Gpu.GetInput(1, 0), Gpu.GetInput(1, 1), Gpu.GetInput(1, 2), 1f);
            }

            [SpirvFragmentShader]
            public static void Color()
                => Gpu.SetOutput(0, Gpu.GetInput(0, 0), Gpu.GetInput(0, 1), Gpu.GetInput(0, 2), 1f);

            [StructLayout(LayoutKind.Sequential)]
            private struct MeshVertex(float4 position, float4 color)
            {
                public float4 Position = position;
                public float4 Color = color;
            }

            private sealed class DrawResources : IDisposable
            {
                private static readonly MeshVertex[] Vertices = [
                    new(new float4(-0.65f, -0.65f, 0, 1), new float4(1, 0.2f, 0.1f, 1)),
                    new(new float4(0.65f, -0.65f, 0, 1), new float4(0.1f, 1, 0.2f, 1)),
                    new(new float4(0.65f, 0.65f, 0, 1), new float4(0.2f, 0.1f, 1, 1)),
                    new(new float4(-0.65f, 0.65f, 0, 1), new float4(1, 0.8f, 0.1f, 1)),
                ];
                private static readonly uint[] Indices = [0, 1, 2, 2, 3, 0];
                public EditorPipelineFrame Frame;
                public Controls Controls;
                public Material Material;
                private WgpuHandle<WGPURenderPipeline> _pipeline;
                private WgpuHandle<WGPUBuffer> _vertices;
                private WgpuHandle<WGPUBuffer> _indices;

                public void Clear(WgpuReactiveRenderGraphPassContext pass)
                {
                    var blue = Material.Animate ? 0.5f + 0.5f * math.sin((float)Frame.Elapsed) : 0.5f;
                    var color = new float3(Controls.X * Material.Tint.x, Controls.Y * Material.Tint.y, blue * Material.Tint.z) * Material.Exposure;
                    pass.GetOrBeginRenderPass(new WgpuReactiveRenderGraphColorAttachment(Frame.Output, WGPULoadOp.Clear,
                        ClearValue: new WGPUColor { R = color.x, G = color.y, B = color.z, A = 1 }));
                }

                public void Mesh(WgpuReactiveRenderGraphPassContext pass)
                {
                    var encoder = pass.GetOrBeginRenderPass(new WgpuReactiveRenderGraphColorAttachment(Frame.Output, WGPULoadOp.Load));
                    Wgpu.SetRenderPipeline(encoder, _pipeline);
                    Wgpu.SetVertexBuffer(encoder, 0, _vertices);
                    Wgpu.SetIndexBuffer(encoder, _indices, WGPUIndexFormat.Uint32);
                    Wgpu.DrawIndexed(encoder, (uint)Indices.Length);
                }

                public unsafe void Create(WgpuHandle<WGPUDevice> device, WgpuHandle<WGPUQueue> queue,
                    WGPUTextureFormat format, string vertexSource, string fragmentSource)
                {
                    var vertex = default(WgpuHandle<WGPUShaderModule>);
                    var fragment = default(WgpuHandle<WGPUShaderModule>);
                    try {
                        vertex = Wgpu.CreateWgslShaderModule(device, vertexSource);
                        fragment = Wgpu.CreateWgslShaderModule(device, fragmentSource);
                        var attributes = stackalloc WGPUVertexAttribute[2] {
                            new() { Format = WGPUVertexFormat.Float32x4, Offset = 0, ShaderLocation = 0 },
                            new() { Format = WGPUVertexFormat.Float32x4, Offset = 16, ShaderLocation = 1 },
                        };
                        var layout = new WGPUVertexBufferLayout {
                            ArrayStride = (ulong)sizeof(MeshVertex), StepMode = WGPUVertexStepMode.Vertex,
                            AttributeCount = 2, Attributes = attributes,
                        };
                        var target = new WGPUColorTargetState { Format = format, WriteMask = WGPUColorWriteMask.All };
                        var fs = new WGPUFragmentState { Module = (WGPUShaderModule*)fragment.Handle,
                            EntryPoint = new WGPUStringView { Length = nuint.MaxValue }, TargetCount = 1, Targets = &target };
                        _pipeline = Wgpu.CreateRenderPipeline(device, new WGPURenderPipelineDescriptor {
                            Vertex = new WGPUVertexState { Module = (WGPUShaderModule*)vertex.Handle,
                                EntryPoint = new WGPUStringView { Length = nuint.MaxValue }, BufferCount = 1, Buffers = &layout },
                            Fragment = &fs,
                            Primitive = new WGPUPrimitiveState { Topology = WGPUPrimitiveTopology.TriangleList, FrontFace = WGPUFrontFace.CCW, CullMode = WGPUCullMode.None },
                            Multisample = new WGPUMultisampleState { Count = 1, Mask = uint.MaxValue },
                        });
                        _vertices = Wgpu.CreateBuffer(device, new WGPUBufferDescriptor {
                            Size = (ulong)(Vertices.Length * sizeof(MeshVertex)), Usage = WGPUBufferUsage.Vertex | WGPUBufferUsage.CopyDst,
                        });
                        Wgpu.WriteBuffer<MeshVertex>(queue, _vertices, 0, Vertices);
                        _indices = Wgpu.CreateBuffer(device, new WGPUBufferDescriptor {
                            Size = (ulong)(Indices.Length * sizeof(uint)), Usage = WGPUBufferUsage.Index | WGPUBufferUsage.CopyDst,
                        });
                        Wgpu.WriteBuffer<uint>(queue, _indices, 0, Indices);
                    }
                    catch { Dispose(); throw; }
                    finally { Wgpu.Release(ref fragment); Wgpu.Release(ref vertex); }
                }

                public void Dispose()
                {
                    Wgpu.Release(ref _pipeline);
                    Wgpu.Release(ref _vertices);
                    Wgpu.Release(ref _indices);
                }
            }
        }
        """";
}
