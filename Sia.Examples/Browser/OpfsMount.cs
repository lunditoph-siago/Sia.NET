#if BROWSER
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Sia_Examples.Browser;

public static partial class OpfsMount
{
    private const int OpfsDirectoryMode = 0x1FF; // 0777
    private const int MaxAttempts = 3;

    public static async Task<bool> MountAsync(string path)
    {
        for (var attempt = 1; attempt <= MaxAttempts; attempt++) {
            if (attempt > 1) {
                await Task.Delay(50 * (attempt - 1));
            }
            try {
                var result = await Task.Run(() => {
                    var backend = NativeMethods.wasmfs_create_opfs_backend();
                    return backend == nint.Zero
                        ? -1
                        : NativeMethods.wasmfs_create_directory(path, OpfsDirectoryMode, backend);
                });
                if (result == 0) {
                    return true;
                }
            }
            catch {
            }
        }
        return false;
    }

    private static partial class NativeMethods
    {
        [LibraryImport("__Internal_emscripten")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial nint wasmfs_create_opfs_backend();

        [LibraryImport("__Internal_emscripten", StringMarshalling = StringMarshalling.Utf8)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial int wasmfs_create_directory(string path, int mode, nint backend);
    }
}
#endif
