using System.Text.Json;
#if BROWSER
using System.Runtime.InteropServices.JavaScript;
#endif

namespace Sia.Examples.Editor;

internal sealed partial class EditorShaderPreview : IDisposable
{
    internal const int k_MaxRenderDimension = 1280;
    private static readonly JsonSerializerOptions s_JsonOptions = new(JsonSerializerDefaults.Web);
#if BROWSER
    private readonly JSObject _handle;
#endif
    private bool _disposed;

    public EditorShaderPreview(string containerId, string statusId, EditorUniformInspector inspector)
    {
#if BROWSER
        _handle = Create(containerId, statusId, inspector.Handle, k_MaxRenderDimension);
#endif
    }

    public void Invalidate(int revision)
    {
#if BROWSER
        if (!_disposed) SetRevision(_handle, revision);
#endif
    }

    public Task<double> ShowAsync(ShaderArtifact artifact, int revision)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (artifact.Preview.UnsupportedReason is { } reason) throw new InvalidOperationException(reason);
#if BROWSER
        return Show(_handle, artifact.Wgsl,
            artifact.EntryName, JsonSerializer.Serialize(artifact.Preview, s_JsonOptions), revision,
            "WGSL");
#else
        return Task.FromException<double>(new PlatformNotSupportedException("GPU preview is available in the browser editor."));
#endif
    }

    public static void Download(ShaderArtifact artifact)
    {
#if BROWSER
        DownloadBinary(artifact.QualifiedName + ".spv", Convert.ToBase64String(artifact.Spirv));
#endif
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
#if BROWSER
        Release(_handle);
        _handle.Dispose();
#endif
    }

#if BROWSER
    [JSImport("createShaderPreview", "main.js")]
    private static partial JSObject Create(string containerId, string statusId, JSObject inspector, int maximumDimension);
    [JSImport("setShaderPreviewRevision", "main.js")]
    private static partial void SetRevision(JSObject owner, int revision);
    [JSImport("showShaderPreview", "main.js")]
    [return: JSMarshalAs<JSType.Promise<JSType.Number>>]
    private static partial Task<double> Show(JSObject owner, string wgsl, string entry,
        string layoutJson, int revision, string route);
    [JSImport("disposeShaderPreview", "main.js")]
    private static partial void Release(JSObject owner);
    [JSImport("downloadShaderBinary", "main.js")]
    private static partial void DownloadBinary(string name, string base64);
#endif
}
