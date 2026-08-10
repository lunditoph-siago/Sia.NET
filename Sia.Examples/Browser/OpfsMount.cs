#if BROWSER
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Sia_Examples.Browser;

public static partial class OpfsMount
{
    private const int OpfsDirectoryMode = 0x1FF; // 0777

    public static async Task<bool> MountAsync(string path)
    {
        var result = await Task.Run(() => {
            var backend = NativeMethods.wasmfs_create_opfs_backend();
            return backend == nint.Zero
                ? -1
                : NativeMethods.wasmfs_create_directory(path, OpfsDirectoryMode, backend);
        });
        return result == 0;
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
