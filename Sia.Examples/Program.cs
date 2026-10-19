using Sia.Examples;
#if !BROWSER
using Sia.Examples.Console;
#endif

#if BROWSER
await ExampleViewer.RunAsync();
#else
await ConsoleApplication.RunAsync();
#endif
