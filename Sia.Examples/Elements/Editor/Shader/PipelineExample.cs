namespace Sia_Examples.Editor;

internal static class PipelineExample
{
    public const string Folder = "pipeline";
    public const string Source = """"
        using System;
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

        public static class Pipeline
        {
            private static readonly RenderGraphPassKey Clear = new("clear");
            private static readonly RenderGraphPassKey Triangle = new("triangle");
            private readonly record struct Controls(bool Triangle, float X, float Y);
            private readonly record struct InputCapture(Entity Window, State<Controls> State);
            private readonly record struct GpuDependencies(WgpuHandle<WGPUDevice> Device, WGPUTextureFormat Format, DrawResources Draw, string Shader);

            // The editor mounts this existing ReactiveComponent delegate in its own ECS world.
            public static ReactiveNode Render(in EditorPipelineFrame frame, ref Hooks hooks)
            {
                var state = hooks.UseState(new Controls(true, 0.5f, 0.5f));
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
                            c.State.Set(c.State.Value with { Triangle = !c.State.Value.Triangle });
                    });
                var draw = hooks.UseRef(static () => new DrawResources()).Value;
                draw.Frame = frame;
                draw.Controls = state.Value;
                hooks.UseRenderGraphPass(frame.Graph, Clear, "Animated clear", frame.Output, DeclareClear);
                hooks.UseWgpuRenderGraphPassHandler(frame.Graph, Clear, draw.Clear);
                return Reactive.When(state.Value.Triangle, Reactive.Component<EditorPipelineFrame>(Overlay, in frame));
            }

            private static void DeclareClear(in RenderGraphTextureKey output, RenderGraphPassDeclarationBuilder pass)
                => pass.Write(output, RenderGraphTextureUsage.RenderAttachment).MarkSideEffect();

            private static ReactiveNode Overlay(in EditorPipelineFrame frame, ref Hooks hooks)
            {
                // A child component mounts/unmounts a real second pass when Space changes state.
                var shader = frame.Shader((Action)Color);
                var draw = hooks.UseRef(static () => new DrawResources()).Value;
                draw.Frame = frame;
                hooks.UseEffect(new GpuDependencies(frame.Device, frame.Format, draw, shader.Source),
                    static (in GpuDependencies d) => { d.Draw.Create(d.Device, d.Format, d.Shader); return d.Draw; },
                    static (in DrawResources d) => d.Dispose());
                hooks.UseRenderGraphPass(frame.Graph, Triangle, "IL fragment triangle", frame.Output, DeclareTriangle);
                hooks.UseWgpuRenderGraphPassHandler(frame.Graph, Triangle, draw.Triangle);
                return Reactive.None;
            }

            private static void DeclareTriangle(in RenderGraphTextureKey output, RenderGraphPassDeclarationBuilder pass)
                => pass.ReadWrite(output, RenderGraphTextureUsage.RenderAttachment)
                    .DependsOn(Clear).MarkSideEffect();

            [SpirvFragmentShader]
            public static void Color() => Gpu.SetOutput(0, 0.95f, 0.55f, 0.15f, 1f);

            private sealed class DrawResources : IDisposable
            {
                public EditorPipelineFrame Frame;
                public Controls Controls;
                private WgpuHandle<WGPURenderPipeline> _pipeline;

                public void Clear(WgpuReactiveRenderGraphPassContext pass)
                {
                    var color = new float3(Controls.X, Controls.Y, 0.5f + 0.5f * math.sin((float)Frame.Elapsed));
                    pass.GetOrBeginRenderPass(new WgpuReactiveRenderGraphColorAttachment(Frame.Output, WGPULoadOp.Clear,
                        ClearValue: new WGPUColor { R = color.x, G = color.y, B = color.z, A = 1 }));
                }

                public void Triangle(WgpuReactiveRenderGraphPassContext pass)
                {
                    var encoder = pass.GetOrBeginRenderPass(new WgpuReactiveRenderGraphColorAttachment(Frame.Output, WGPULoadOp.Load));
                    Wgpu.SetRenderPipeline(encoder, _pipeline);
                    Wgpu.Draw(encoder, 3);
                }

                public unsafe void Create(WgpuHandle<WGPUDevice> device, WGPUTextureFormat format, string fragmentSource)
                {
                    var vertex = Wgpu.CreateWgslShaderModule(device, """
                        @vertex fn vertex(@builtin(vertex_index) i: u32) -> @builtin(position) vec4f {
                            let p = array<vec2f, 3>(vec2f(0.0, 0.7), vec2f(-0.7, -0.7), vec2f(0.7, -0.7));
                            return vec4f(p[i], 0.0, 1.0);
                        }
                        """);
                    var fragment = default(WgpuHandle<WGPUShaderModule>);
                    try {
                        fragment = Wgpu.CreateWgslShaderModule(device, fragmentSource);
                        var target = new WGPUColorTargetState { Format = format, WriteMask = WGPUColorWriteMask.All };
                        var fs = new WGPUFragmentState { Module = (WGPUShaderModule*)fragment.Handle,
                            EntryPoint = new WGPUStringView { Length = nuint.MaxValue }, TargetCount = 1, Targets = &target };
                        _pipeline = Wgpu.CreateRenderPipeline(device, new WGPURenderPipelineDescriptor {
                            Vertex = new WGPUVertexState { Module = (WGPUShaderModule*)vertex.Handle,
                                EntryPoint = new WGPUStringView { Length = nuint.MaxValue } },
                            Fragment = &fs,
                            Primitive = new WGPUPrimitiveState { Topology = WGPUPrimitiveTopology.TriangleList, FrontFace = WGPUFrontFace.CCW, CullMode = WGPUCullMode.None },
                            Multisample = new WGPUMultisampleState { Count = 1, Mask = uint.MaxValue },
                        });
                    }
                    finally { Wgpu.Release(ref fragment); Wgpu.Release(ref vertex); }
                }

                public void Dispose() => Wgpu.Release(ref _pipeline);
            }
        }
        """";
}
