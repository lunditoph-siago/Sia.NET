using Sia_Examples;
#if !BROWSER
using Sia_Examples.Console;
using Sia_Examples.Notebook;
#endif

#if BROWSER
await ExampleViewer.RunAsync();
#else
await ConsoleApplication.RunAsync(args, new NotebookLibrary());
#endif
