using Sia_Examples.Editor;

namespace Sia_Examples.Notebook;

public static class WorkspaceSeed
{
    public const string FolderPath = "src";
    public const string ShaderPath = "src/Shader.cs";

    public static IReadOnlyList<(string Path, string Content)> Files =>
        [(ShaderPath, CompilerTemplate.Source)];

    public static async ValueTask EnsureSeededAsync(
        IWorkspaceStorage storage, CancellationToken cancellationToken = default)
    {
        if ((await storage.ListTreeAsync(cancellationToken)).Count > 0) return;
        await storage.CreateFolderAsync(FolderPath, cancellationToken);
        await storage.CreateFileAsync(ShaderPath, CompilerTemplate.Source, cancellationToken);
    }
}
