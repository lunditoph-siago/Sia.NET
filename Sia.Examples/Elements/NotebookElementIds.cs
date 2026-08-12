namespace Sia_Examples.Notebook;

public static class NotebookElementIds
{
    public static string Editor(string cellId) => $"editor-{cellId}";

    public static string Paragraph(string blockId) => $"paragraph-{blockId}";

    public static string SectionTitleInput(string sectionId) => $"section-title-input-{sectionId}";

    public static string ScopeInput(string cellId) => $"scope-{cellId}";
}
