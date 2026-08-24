namespace Sia_Examples.Editor;

public static class CompletionIdentifier
{
    public static int FindStart(Text document, int position)
    {
        position = Math.Clamp(position, 0, document.Length);
        var line = document.LineAt(position);
        var column = position - line.From;
        while (column > 0 && IsCharacter(line.Text[column - 1])) {
            column--;
        }
        return line.From + column;
    }

    public static int FindStart(string source, int position)
    {
        position = Math.Clamp(position, 0, source.Length);
        while (position > 0 && IsCharacter(source[position - 1])) {
            position--;
        }
        return position;
    }

    public static bool IsCharacter(char character)
        => char.IsLetterOrDigit(character) || character == '_';
}
