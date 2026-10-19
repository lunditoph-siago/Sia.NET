#if !BROWSER
namespace Sia.Examples.Console;

internal readonly record struct EditCursor(ConsoleDomNode Line, int Column);
#endif
