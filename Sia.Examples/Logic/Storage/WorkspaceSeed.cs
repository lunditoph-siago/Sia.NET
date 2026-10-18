using Sia_Examples.Editor;

namespace Sia_Examples.Notebook;

public static class WorkspaceSeed
{
    public const string FolderPath = "src";
    public const string ProgramPath = "src/Program.cs";

    public static IReadOnlyList<(string Path, string Content)> Files =>
        [(ProgramPath, CompilerTemplate.ProgramSource),
         ("src/MeshShaders.cs", CompilerTemplate.ShaderSource),
         ("src/DrawResources.cs", CompilerTemplate.ResourceSource)];

    public static async ValueTask EnsureSeededAsync(
        IWorkspaceStorage storage, CancellationToken cancellationToken = default)
    {
        if ((await storage.ListTreeAsync(cancellationToken)).Count > 0) return;
        await storage.CreateFolderAsync(FolderPath, cancellationToken);
        foreach (var (path, content) in Files)
            await storage.CreateFileAsync(path, content, cancellationToken);
    }
}
