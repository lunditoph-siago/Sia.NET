#if !BROWSER
using Sia_Examples;

ExampleViewer.Run();
#else
using Sia_Examples;

ExampleViewer.Render();
await ExampleViewer.Run();
#endif
