namespace Sia.Examples.Editor;

internal readonly record struct ModalPending(
    int Count = 0, EditorCommandId? Operator = null, int OperatorCount = 0, KeyStroke? Prefix = null);

internal readonly record struct ModalInvocation(EditorCommandId Id, int Count = 1, EditorCommandId? Motion = null, bool Counted = false);

internal readonly record struct ModalTransition(ModalPending Pending, ModalInvocation? Invocation = null);

internal static class ModalInput
{
    internal const int k_MaximumCount = 9999;

    internal static KeyStroke Normalize(string key, ShortcutModifiers modifiers)
    {
        if (key.Length != 1) return new(key, modifiers);
        var shifted = char.IsUpper(key[0]);
        modifiers &= ~ShortcutModifiers.Shift;
        if (shifted) modifiers |= ShortcutModifiers.Shift;
        return new(key == " " ? "Space" : key.ToUpperInvariant(), modifiers);
    }

    internal static ModalTransition Parse(ModalPending pending, KeyStroke key, IReadOnlyList<CommandBinding> bindings,
        EditorInputMode mode, CommandContext context = CommandContext.Editor, KeyStroke? physicalKey = null)
    {
        if (key.Key == "Escape") return new(default, new(EditorCommandId.Escape));
        var modal = mode is EditorInputMode.Normal or EditorInputMode.Visual;
        if (modal && pending.Prefix is null && key.Modifiers == ShortcutModifiers.None && key.Key is [>= '0' and <= '9']
            && (physicalKey is null || physicalKey.Value.Modifiers == ShortcutModifiers.None)
            && (key.Key != "0" || pending.Count != 0)) {
            var count = System.Math.Min(k_MaximumCount, pending.Count * 10 + key.Key[0] - '0');
            return new(pending with { Count = count });
        }
        var eligible = bindings.Where(binding => EditorKeymap.Allows(binding.When, context)
            && (binding.Mode is null || binding.Mode == mode));
        if (pending.Prefix is { } prefix) {
            eligible = eligible.Where(binding => binding.Keys.Length == 2 && EditorKeymap.Matches(binding.Keys[0], prefix)
                && Matches(binding.Keys[1]));
        }
        else eligible = eligible.Where(binding => Matches(binding.Keys[0]));
        var matched = eligible.OrderByDescending(static binding => binding.Priority)
            .ThenByDescending(static binding => binding.Mode is not null).FirstOrDefault();
        if (matched is null) return new(default);
        if (pending.Prefix is null && matched.Keys.Length == 2) return new(pending with { Prefix = matched.Keys[0] });
        var commandId = matched.Command;
        if (!modal) return new(default, new(commandId));
        var countValue = System.Math.Max(1, pending.Count);
        var counted = pending.Count > 0 || pending.OperatorCount > 0;
        if (IsOperator(commandId)) {
            if (mode == EditorInputMode.Visual) return new(default, new(commandId, countValue, Counted: counted));
            if (pending.Operator is { } operation) {
                return operation == commandId
                    ? new(default, new(operation, Multiply(pending.OperatorCount, countValue), Counted: counted))
                    : new(default);
            }
            return new(new(Operator: commandId, OperatorCount: pending.Count));
        }
        if (pending.Operator is { } op) {
            return IsMotion(commandId)
                ? new(default, new(op, Multiply(pending.OperatorCount, countValue), commandId, counted))
                : new(default);
        }
        return new(default, new(commandId, countValue, Counted: counted));

        bool Matches(KeyStroke stroke) => stroke.Key.StartsWith('[')
            ? physicalKey is { } physical && EditorKeymap.Matches(stroke, physical)
            : EditorKeymap.Matches(stroke, key);
    }

    internal static bool IsMotion(EditorCommandId commandId) => commandId is
        EditorCommandId.Left or EditorCommandId.Right or EditorCommandId.Up or EditorCommandId.Down
        or EditorCommandId.WordLeft or EditorCommandId.WordRight or EditorCommandId.LineStart
        or EditorCommandId.LineEnd or EditorCommandId.Start or EditorCommandId.End;

    internal static bool IsOperator(EditorCommandId commandId) => commandId is
        EditorCommandId.DeleteOperator or EditorCommandId.ChangeOperator or EditorCommandId.IndentOperator or EditorCommandId.OutdentOperator;

    private static int Multiply(int first, int second) => System.Math.Min(k_MaximumCount, System.Math.Max(1, first) * second);
}
