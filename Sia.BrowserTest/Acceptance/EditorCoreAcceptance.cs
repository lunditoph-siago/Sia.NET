using Sia_Examples.Editor;

namespace Sia_BrowserTest.Acceptance;

public sealed class EditorCoreAcceptance : IAcceptanceStage
{
    public string Name => "1. Editor core";

    public async Task RunAsync(AcceptanceContext context)
    {
        await context.CaseAsync("text tree preserves lines and offsets", TestTextAsync);
        await context.CaseAsync("change set applies ordered replacements", TestChangesAsync);
        await context.CaseAsync("DOM diff follows native selection intent", TestDomDiffAsync);
        await context.CaseAsync("selection maps through insertion", TestSelectionAsync);
        await context.CaseAsync("editor commands compose state changes", TestCommandsAsync);
        await context.CaseAsync("line identities survive local edits", TestLineIdentitiesAsync);
        await context.CaseAsync("decorations segment only marked spans", TestDecorationsAsync);
        await context.CaseAsync(
            "completion activates only for a useful prefix",
            TestCompletionTriggerAsync);
    }

    private static Task TestTextAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var text = Text.OfString("alpha\nbeta\ngamma");
        AcceptanceAssert.Equal(16, text.Length);
        AcceptanceAssert.Equal(3, text.Lines);
        AcceptanceAssert.Equal(new Line(6, 10, 2, "beta"), text.Line(2));
        AcceptanceAssert.Equal("beta", text.SliceDoc(6, 10));
        return Task.CompletedTask;
    }

    private static Task TestChangesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var document = Text.OfString("alpha\nbeta\ngamma");
        var changes = ChangeSet.Of(
            [new(1, 4, "LPH"), new(6, 10, "B")],
            document.Length);
        AcceptanceAssert.Equal("aLPHa\nB\ngamma", changes.Apply(document).SliceDoc());
        return Task.CompletedTask;
    }

    private static Task TestDomDiffAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        AcceptanceAssert.Equal(
            new TextDifference(1, 1, ";"),
            TextDiff.Find("a;", "a;;", 1));
        AcceptanceAssert.Equal(
            new TextDifference(1, 2, string.Empty),
            TextDiff.Find(
                "a;;",
                "a;",
                2,
                TextDiff.Preference.End));
        AcceptanceAssert.Equal(
            new TextDifference(1, 2, string.Empty),
            TextDiff.Find("a;;", "a;", 1));
        AcceptanceAssert.Equal(
            new TextDifference(6, 11, "X"),
            TextDiff.FindForSelection("alpha alpha", "alpha X", 6, 11));
        AcceptanceAssert.Equal<TextDifference?>(
            null,
            TextDiff.FindForSelection("same", "same", 1, 3));
        return Task.CompletedTask;
    }

    private static Task TestSelectionAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var selection = EditorSelection.Single(6);
        var change = ChangeSet.Of([new(0, 0, "++")], 16);
        AcceptanceAssert.Equal(8, selection.Map(change).Main.Head);
        return Task.CompletedTask;
    }

    private static Task TestCommandsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var state = EditorState.Create("alpha\nbeta", DecorationSet.Empty);
        var target = new CommandTarget(state, next => state = next);

        AcceptanceAssert.True(CursorCommands.DocumentEnd(target), "DocumentEnd did not run.");
        AcceptanceAssert.Equal(10, state.Selection.Main.Head);

        target = new(state, next => state = next);
        AcceptanceAssert.True(
            CursorCommands.SelectDocumentStart(target),
            "SelectDocumentStart did not run.");
        AcceptanceAssert.Equal(10, state.Selection.Main.Anchor);
        AcceptanceAssert.Equal(0, state.Selection.Main.Head);

        state = EditorState.Create("foo", DecorationSet.Empty);
        target = new(state, next => state = next);
        AcceptanceAssert.True(CursorCommands.SelectLineEnd(target), "SelectLineEnd did not run.");
        AcceptanceAssert.Equal(0, state.Selection.Main.Anchor);
        AcceptanceAssert.Equal(3, state.Selection.Main.Head);

        state = EditorState.Create("alpha\nbeta", DecorationSet.Empty).Apply(new() {
            Selection = EditorSelection.Single(10),
        });
        target = new(state, next => state = next);
        AcceptanceAssert.True(DeleteCommands.CharBackward(target), "Backspace did not run.");
        AcceptanceAssert.Equal("alpha\nbet", state.Doc.SliceDoc());

        state = EditorState.Create("  alpha", DecorationSet.Empty).Apply(new() {
            Selection = EditorSelection.Single(7),
        });
        target = new(state, next => state = next);
        AcceptanceAssert.True(
            LineCommands.InsertNewlineAndIndent(target),
            "Newline command did not run.");
        AcceptanceAssert.Equal("  alpha\n  ", state.Doc.SliceDoc());

        state = EditorState.Create("foo\nbar", DecorationSet.Empty).Apply(new() {
            Selection = EditorSelection.Single(7, 3),
        });
        target = new(state, next => state = next);
        AcceptanceAssert.True(TextCommands.Insert(target, "X"), "Text insertion did not run.");
        AcceptanceAssert.Equal("fooX", state.Doc.SliceDoc());
        AcceptanceAssert.Equal(4, state.Selection.Main.Head);

        state = EditorState.Create("a;", DecorationSet.Empty).Apply(new() {
            Selection = EditorSelection.Single(1),
        });
        target = new(state, next => state = next);
        AcceptanceAssert.True(TextCommands.Insert(target, ";"), "Text insertion did not run.");
        AcceptanceAssert.Equal("a;;", state.Doc.SliceDoc());
        AcceptanceAssert.Equal(2, state.Selection.Main.Head);

        state = EditorState.Create("Console.Write", DecorationSet.Empty).Apply(new() {
            Selection = EditorSelection.Single(13),
        });
        target = new(state, next => state = next);
        AcceptanceAssert.True(CursorCommands.GroupLeft(target), "GroupLeft did not run.");
        AcceptanceAssert.Equal(8, state.Selection.Main.Head);

        target = new(state, next => state = next);
        AcceptanceAssert.True(DeleteCommands.GroupBackward(target), "GroupBackward did not run.");
        AcceptanceAssert.Equal("ConsoleWrite", state.Doc.SliceDoc());

        state = EditorState.Create("Console.Write", DecorationSet.Empty).Apply(new() {
            Selection = EditorSelection.Single(13),
        });
        target = new(state, next => state = next);
        AcceptanceAssert.True(DeleteCommands.GroupBackward(target), "GroupBackward did not run.");
        AcceptanceAssert.Equal("Console.", state.Doc.SliceDoc());

        state = EditorState.Create("one two", DecorationSet.Empty).Apply(new() {
            Selection = EditorSelection.Single(3),
        });
        target = new(state, next => state = next);
        AcceptanceAssert.True(DeleteCommands.GroupForward(target), "GroupForward did not run.");
        AcceptanceAssert.Equal("one", state.Doc.SliceDoc());
        return Task.CompletedTask;
    }

    private static Task TestLineIdentitiesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var state = EditorState.Create("one\ntwo\nthree", DecorationSet.Empty);
        var replaced = state.Apply(new() { Changes = [new(4, 7, "TWO")] });
        AcceptanceAssert.SequenceEqual([0, 1, 2], replaced.LineIdentities.Values);
        AcceptanceAssert.True(
            ReferenceEquals(state.LineIdentities.Values, replaced.LineIdentities.Values),
            "A local edit rebuilt unchanged line identities.");

        var split = replaced.Apply(new() { Changes = [new(5, 5, "\n")] });
        AcceptanceAssert.SequenceEqual([0, 1, 3, 2], split.LineIdentities.Values);
        AcceptanceAssert.True(
            !ReferenceEquals(replaced.LineIdentities.Values, split.LineIdentities.Values),
            "A structural edit reused stale line identities.");
        return Task.CompletedTask;
    }

    private static Task TestDecorationsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var runs = LineDecorator.Segment(
            "abcd",
            0,
            [new(1, 3, Decoration.Mark("mark"))]);
        AcceptanceAssert.SequenceEqual(
            [new StyledRun("a", null), new("bc", "mark"), new("d", null)],
            runs);
        return Task.CompletedTask;
    }

    private static Task TestCompletionTriggerAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        AcceptanceAssert.True(
            !CompletionTrigger.ShouldActivate(StateAtEnd(""), StateAtEnd("C"), false),
            "The first identifier character activated completion.");
        AcceptanceAssert.True(
            CompletionTrigger.ShouldActivate(StateAtEnd("C"), StateAtEnd("Co"), false),
            "A useful identifier prefix did not activate completion.");
        AcceptanceAssert.True(
            CompletionTrigger.ShouldActivate(
                StateAtEnd("Console"),
                StateAtEnd("Console."),
                false),
            "Member access did not activate completion.");
        AcceptanceAssert.True(
            CompletionTrigger.ShouldActivate(
                StateAtEnd("Console."),
                StateAtEnd("Console.W"),
                true),
            "An active member completion did not continue while typing.");
        AcceptanceAssert.True(
            CompletionTrigger.ShouldActivate(
                StateAtEnd(string.Empty),
                StateAtEnd("Console.W"),
                false),
            "A coalesced member insertion did not activate completion.");
        AcceptanceAssert.Equal(
            CompletionIdentifier.FindStart(Text.OfString("Console."), 8),
            CompletionIdentifier.FindStart(Text.OfString("Console.W"), 9),
            "Typing after member access moved the completion query anchor.");
        AcceptanceAssert.True(
            CompletionTrigger.ShouldActivate(
                StateAtEnd("Console.W"),
                StateAtEnd("Console."),
                true),
            "Backspace did not preserve a valid member completion.");
        AcceptanceAssert.True(
            !CompletionTrigger.ShouldActivate(StateAtEnd("Co"), StateAtEnd("C"), true),
            "Backspace preserved an identifier prefix that is too short.");
        AcceptanceAssert.True(
            !CompletionTrigger.ShouldActivate(StateAtEnd("Co"), StateAtEnd("Co;"), true),
            "Punctuation preserved an unrelated completion.");

        var source = new CompletionResult([
            new("ReadLine", "ReadLine", 8, 8),
            new("Write", "Write", 8, 8),
            new("WriteLine", "WriteLine", 8, 8),
        ]);
        AcceptanceAssert.True(
            source.TryFilter(Text.OfString("Console.W"), 9, 20, out var filtered),
            "A valid completion prefix did not reuse its source result.");
        AcceptanceAssert.SequenceEqual(
            ["Write", "WriteLine"],
            filtered.Items.Select(static item => item.Label));
        AcceptanceAssert.True(
            filtered.Items.All(static item => item.ReplaceEnd == 9),
            "Filtered candidates did not map their replacement end.");
        AcceptanceAssert.True(
            !source.TryFilter(Text.OfString("Console.W;"), 10, 20, out _),
            "An invalid completion prefix reused its source result.");
        return Task.CompletedTask;
    }

    private static EditorState StateAtEnd(string document)
        => EditorState.Create(document, DecorationSet.Empty).Apply(new() {
            Selection = EditorSelection.Single(document.Length),
        });
}
