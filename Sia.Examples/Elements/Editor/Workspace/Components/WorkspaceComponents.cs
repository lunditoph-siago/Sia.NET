namespace Sia_Examples.Editor;

using Sia;
using Sia.Reactors;

public sealed class WorkspaceTreeTag;

public partial record struct WorkspaceEntry([Sia] string Name);

public partial record struct WorkspaceFile
{
    public WorkspaceFile() { }

    [Sia]
    public string Content { get; set; } = string.Empty;

    public string SavedContent { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;

    public bool IsDirty => Content != SavedContent;
}

public partial record struct WorkspaceFolder([Sia] bool Expanded);

public record struct OpenEditorFile;

public record struct PreviewTab;

public record struct UntitledFile;

public readonly record struct EditorMemento(int Line, int Column, double ScrollTop);
