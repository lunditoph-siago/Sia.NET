using Sia_Examples.Notebook;

namespace Sia_Examples;

public readonly record struct ExampleAppProps(
    NotebookLibrary Library,
    IRenderHost<ExampleItemView> Host);
