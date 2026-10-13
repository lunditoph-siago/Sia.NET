#if BROWSER
using System.Runtime.InteropServices;
using Sia.WebGPU;
using Sia.Window;

namespace Sia_Examples.Editor;

internal sealed partial class EditorPipelineSession
{
    private WgpuHandle<WGPUTexture> _outputTexture;
    private WgpuHandle<WGPUBuffer> _readback;
    private ulong _maxReadbackBytes;
    private int _bytesPerRow;

    private void ResizeReadback(int width, int height)
    {
        var rowBytes = checked(width * 4);
        var stride = checked((rowBytes + 255) & ~255);
        var bufferBytes = checked((ulong)stride * (ulong)height);
        if (bufferBytes > _maxReadbackBytes || bufferBytes > int.MaxValue)
            throw new InvalidOperationException("Canvas exceeds the GPU readback buffer limit.");
        ReleaseReadback();
        _bytesPerRow = stride;
        _outputTexture = Wgpu.CreateTexture(_device, new WGPUTextureDescriptor {
            Dimension = WGPUTextureDimension._2D,
            Size = new WGPUExtent3D { Width = (uint)width, Height = (uint)height, DepthOrArrayLayers = 1 },
            Format = WGPUTextureFormat.RGBA8Unorm,
            MipLevelCount = 1, SampleCount = 1,
            Usage = WGPUTextureUsage.RenderAttachment | WGPUTextureUsage.CopySrc,
        });
        _readback = Wgpu.CreateBuffer(_device, new WGPUBufferDescriptor {
            Size = bufferBytes, Usage = WGPUBufferUsage.CopyDst | WGPUBufferUsage.MapRead,
        });
    }

    private unsafe void BeginReadback()
    {
        var size = _window.Get<WindowState>().FramebufferSize;
        var encoder = Wgpu.CreateCommandEncoder(_device);
        var commands = default(WgpuHandle<WGPUCommandBuffer>);
        try {
            Wgpu.CopyTextureToBuffer(encoder, _outputTexture, _readback,
                (uint)size.Width, (uint)size.Height, (uint)_bytesPerRow);
            commands = Wgpu.FinishCommandEncoder(encoder, new WGPUCommandBufferDescriptor());
            Wgpu.Submit(_queue, [commands]);
        }
        finally { Wgpu.Release(ref commands); Wgpu.Release(ref encoder); }
        fixed (byte* selector = _selectorUtf8)
            ReadPixels(selector, _readback.Handle, size.Width, size.Height, _bytesPerRow);
    }

    private unsafe bool CompleteReadback()
    {
        fixed (byte* selector = _selectorUtf8) return PixelsPending(selector) == 0;
    }

    private void ReleaseReadback()
    {
        try {
            if (!_readback.IsNull) Wgpu.DestroyBuffer(_readback);
            if (!_outputTexture.IsNull) Wgpu.DestroyTexture(_outputTexture);
        }
        finally { Wgpu.Release(ref _readback); Wgpu.Release(ref _outputTexture); }
    }

    [DllImport("__Internal_emscripten", EntryPoint = "siaEditorReadPixels")]
    private static extern unsafe void ReadPixels(byte* selector, nint buffer, int width, int height, int stride);
    [DllImport("__Internal_emscripten", EntryPoint = "siaEditorPixelsPending")]
    private static extern unsafe int PixelsPending(byte* selector);
}
#endif
