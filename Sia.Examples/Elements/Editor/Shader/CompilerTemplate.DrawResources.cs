namespace Sia_Examples.Editor;

internal static partial class CompilerTemplate
{
    public const string ResourceSource = """"
        using System;
        using System.Runtime.InteropServices;
        using Sia.Math;
        using Sia.Graphics.Reactive;
        using Sia.WebGPU;
        using Sia_Examples.Editor;

        internal sealed class DrawResources : IDisposable
        {
            [StructLayout(LayoutKind.Sequential)]
            private struct MeshVertex(float4 position, float4 color)
            {
                public float4 Position = position;
                public float4 Color = color;
            }

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
        """";
}
