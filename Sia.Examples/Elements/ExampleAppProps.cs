using Sia.Examples.Notebook;

namespace Sia.Examples;

public readonly record struct ExampleAppProps(
    NotebookLibrary Library,
    IRenderHost<ExampleItemView> Host);
