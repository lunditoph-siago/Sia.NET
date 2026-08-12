using Sia_Examples;
#if !BROWSER
using Sia_Examples.Console;
#endif

#if BROWSER
await ExampleViewer.RunAsync();
#else
await ConsoleApplication.RunAsync();
#endif
