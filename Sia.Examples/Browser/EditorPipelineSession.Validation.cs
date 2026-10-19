#if BROWSER
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Sia.WebGPU;

namespace Sia.Examples.Editor;

internal sealed partial class EditorPipelineSession
{
    private Task? _firstFrameValidation;

    private unsafe Task EndFirstFrameValidation()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handle = GCHandle.Alloc(completion);
        try {
            WgpuUnsafe.wgpuDevicePopErrorScope((WGPUDevice*)_device.Handle, new WGPUPopErrorScopeCallbackInfo {
                Mode = WGPUCallbackMode.AllowSpontaneous,
                Callback = &OnFirstFrameValidation,
                Userdata1 = (void*)GCHandle.ToIntPtr(handle),
            });
            return completion.Task;
        }
        catch { handle.Free(); throw; }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void OnFirstFrameValidation(WGPUPopErrorScopeStatus status, WGPUErrorType error,
        WGPUStringView message, void* userdata1, void* userdata2)
    {
        var handle = GCHandle.FromIntPtr((nint)userdata1);
        var completion = (TaskCompletionSource)handle.Target!;
        try {
            if (status == WGPUPopErrorScopeStatus.Success && error == WGPUErrorType.NoError) completion.TrySetResult();
            else {
                var details = message.Data == null ? "" : message.Length == nuint.MaxValue
                    ? Marshal.PtrToStringUTF8((nint)message.Data)
                    : Marshal.PtrToStringUTF8((nint)message.Data, checked((int)message.Length));
                completion.TrySetException(new WgpuException($"First frame validation failed: {status}, {error}. {details}"));
            }
        }
        catch (Exception exception) { completion.TrySetException(exception); }
        finally { handle.Free(); }
    }
}
#endif
