namespace Sia_Examples.Editor;

[Flags]
internal enum SelFlag : byte
{
    BidiMask = 7,
    AssocBefore = 8,
    AssocAfter = 16,
    Inverted = 32,
    Undirectional = 64,
}
