namespace Sia_Examples.Editor;

internal static partial class CompilerTemplate
{
    public const string ProgramSource = """"
        using System;
        using Sia;
        using Sia.Input;
        using Sia.Math;
        using Sia.Reactive;
        using Sia.RenderGraph;
        using Sia.Graphics.Reactive;
        using Sia.WebGPU;
        using Sia.Window;
        using Sia_Examples.Editor;

        public static class Program
        {
            private static readonly RenderGraphPassKey Clear = new("clear");
            private static readonly RenderGraphPassKey Mesh = new("mesh");

            private readonly record struct InputCapture(Entity Window, State<Controls> State);
            private readonly record struct GpuDependencies(WgpuHandle<WGPUDevice> Device, WgpuHandle<WGPUQueue> Queue,
                WGPUTextureFormat Format, DrawResources Draw, string Vertex, string Fragment);

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
                var vertex = frame.Shader((Action)MeshShaders.Vertex);
                var fragment = frame.Shader((Action)MeshShaders.Color);
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
        }

        internal readonly record struct Controls(bool Mesh, float X, float Y);

        internal struct Material
        {
            [Sia.Color] public float4 Tint;
            [Sia.Range(0, 2)] public float Exposure;
            public bool Animate;
        }
        """";
}
