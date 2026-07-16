#if !BROWSER
using Sia_Examples;

ExampleViewer.Run(args);
#else
using Sia_Examples;

await ExampleViewer.Run();
#endif
