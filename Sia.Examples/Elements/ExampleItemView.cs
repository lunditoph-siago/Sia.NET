namespace Sia_Examples;

public readonly record struct ExampleItemView(
    int Index,
    string Name,
    string Description,
    Notebook.NotebookOrigin Origin,
    bool Active);
