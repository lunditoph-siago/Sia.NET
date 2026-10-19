namespace Sia.Examples.Editor;

internal readonly record struct CompletionEdit(ChangeSpec[] Changes, int Position)
{
    internal CompletionEdit WithCharacter(char character, int sourceLength, bool includesCharacter = false)
    {
        if (includesCharacter) return this;
        var changes = ChangeSet.Of(Changes, sourceLength);
        var insertion = ChangeSet.Of([new(Position, Position, character.ToString())], changes.MapPos(sourceLength, 1)!.Value);
        return new(ChangeSet.Compose(changes, insertion).ToSpecs(), Position + 1);
    }
}
