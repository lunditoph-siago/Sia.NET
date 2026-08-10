namespace Sia_Examples.Notebook;

public enum NotebookOrigin
{
    BuiltIn,
    User,
}

public sealed record NotebookInfo(string Name, string Description, string Key, NotebookOrigin Origin);
