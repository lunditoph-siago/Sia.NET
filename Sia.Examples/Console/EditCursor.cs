#if !BROWSER
namespace Sia_Examples.Console;

internal readonly record struct EditCursor(ConsoleDomNode Line, int Column);
#endif
