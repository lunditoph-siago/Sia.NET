using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sia.Examples.Editor;

[Flags]
internal enum ShortcutModifiers { None = 0, Control = 1, Meta = 2, Alt = 4, Shift = 8, Primary = 16 }

internal readonly record struct KeyStroke(string Key, ShortcutModifiers Modifiers)
{
    internal static KeyStroke Parse(string value)
    {
        var parts = value.Split('+', StringSplitOptions.TrimEntries);
        var modifiers = ShortcutModifiers.None;
        foreach (var part in parts[..^1]) {
            var modifier = part.ToUpperInvariant() switch {
                "CTRL" or "CONTROL" => ShortcutModifiers.Control,
                "META" or "CMD" or "COMMAND" => ShortcutModifiers.Meta,
                "ALT" or "OPTION" => ShortcutModifiers.Alt,
                "SHIFT" => ShortcutModifiers.Shift,
                "MOD" => ShortcutModifiers.Primary,
                _ => throw new ArgumentException($"Unknown modifier '{part}'."),
            };
            if ((modifiers & modifier) != 0) throw new ArgumentException($"Repeated modifier '{part}'.");
            modifiers |= modifier;
        }
        if ((modifiers & ShortcutModifiers.Primary) != 0 && (modifiers & (ShortcutModifiers.Control | ShortcutModifiers.Meta)) != 0)
            throw new ArgumentException("Use Mod or an explicit Ctrl/Cmd modifier, not both.");
        var key = parts[^1].ToUpperInvariant() switch {
            "SPACE" => "Space",
            "TAB" => "Tab",
            "ENTER" or "RETURN" => "Enter",
            "ESC" or "ESCAPE" => "Escape",
            "LEFT" or "ARROWLEFT" => "ArrowLeft",
            "RIGHT" or "ARROWRIGHT" => "ArrowRight",
            "UP" or "ARROWUP" => "ArrowUp",
            "DOWN" or "ARROWDOWN" => "ArrowDown",
            "HOME" => "Home",
            "END" => "End",
            "PAGEUP" => "PageUp",
            "PAGEDOWN" => "PageDown",
            "BACKSPACE" => "Backspace",
            "DELETE" or "DEL" => "Delete",
            "INSERT" => "Insert",
            "PLUS" => "+",
            var name when name.Length == 1 => name,
            var name when name.StartsWith('F') && int.TryParse(name.AsSpan(1), out var number) && number is >= 1 and <= 24 => "F" + number,
            var name when name.StartsWith('[') && name.EndsWith(']') && name.Length > 2
                && name[1..^1].All(char.IsAsciiLetterOrDigit) => name,
            _ => throw new ArgumentException($"Unknown key '{parts[^1]}'. Use Space for the space bar or [KeyS] for a physical key."),
        };
        return new(key, modifiers);
    }

    public override string ToString()
    {
        var modifiers = Modifiers;
        return string.Concat(new[] { (ShortcutModifiers.Primary, "Mod+"), (ShortcutModifiers.Control, "Ctrl+"),
            (ShortcutModifiers.Meta, "Cmd+"), (ShortcutModifiers.Alt, "Alt+"), (ShortcutModifiers.Shift, "Shift+") }
            .Where(pair => (modifiers & pair.Item1) != 0).Select(static pair => pair.Item2)) + (Key == "+" ? "Plus" : Key);
    }
}

internal sealed record KeyBinding(string Key, CommandContext? When = null, EditorInputMode? Mode = null)
{
    internal KeyStroke[] Strokes => Key.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(KeyStroke.Parse).ToArray();
}

internal sealed record CommandBinding(EditorCommandId Command,
    [property: JsonConverter(typeof(JsonStringEnumConverter<CommandContext>))] CommandContext When,
    KeyStroke[] Keys, bool Editor, EditorInputMode? Mode = null)
{
    public int Priority => When switch { CommandContext.Workbench => 0, CommandContext.Editor => 1, CommandContext.Suggestions => 3, _ => 2 };
}

internal sealed record EditorKeymap(
    IReadOnlyDictionary<string, KeyBinding[]> Standard,
    IReadOnlyDictionary<string, KeyBinding[]> Vim,
    KeyboardProfile Profile = KeyboardProfile.Standard)
{
    internal static EditorKeymap Default { get; } = new(
        new Dictionary<string, KeyBinding[]>(), new Dictionary<string, KeyBinding[]>());
    internal IReadOnlyDictionary<string, KeyBinding[]> Overrides => Profile == KeyboardProfile.Standard ? Standard : Vim;
    internal EditorInputMode InitialMode => Profile == KeyboardProfile.Vim ? EditorInputMode.Normal : EditorInputMode.Standard;
    internal static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web) {
        Converters = { new JsonStringEnumConverter() },
    };

    internal KeyBinding[] Bindings(EditorCommand command)
        => Overrides.TryGetValue(command.Id.ToString(), out var value) ? value.ToArray() : EditorDefaultBindings.For(command.Id, Profile);

    internal EditorKeymap Select(KeyboardProfile profile)
        => Enum.IsDefined(profile) ? this with { Profile = profile } : throw new ArgumentException("Unknown keyboard profile.");

    internal EditorKeymap Reset()
        => Profile == KeyboardProfile.Standard ? this with { Standard = new Dictionary<string, KeyBinding[]>() }
            : this with { Vim = new Dictionary<string, KeyBinding[]>() };

    internal CommandBinding[] Compile()
        => EditorCommand.All.SelectMany(command => Bindings(command).Select(binding =>
            new CommandBinding(command.Id, binding.When ?? command.Context, binding.Strokes, command.IsEditor, binding.Mode))).ToArray();

    internal EditorCommand? Resolve(KeyStroke stroke, CommandContext context, EditorInputMode mode = EditorInputMode.Standard)
        => Resolve(Compile(), stroke, context, mode);

    internal static EditorCommand? Resolve(IReadOnlyList<CommandBinding> bindings, KeyStroke stroke,
        CommandContext context, EditorInputMode mode = EditorInputMode.Standard)
        => bindings.Where(binding => binding.Keys.Length == 1 && Allows(binding.When, context)
                && (binding.Mode is null || binding.Mode == mode) && Matches(binding.Keys[0], stroke))
            .OrderByDescending(static binding => binding.Priority)
            .ThenByDescending(static binding => binding.Mode is not null)
            .Select(static binding => EditorCommand.Get(binding.Command)).FirstOrDefault();

    internal EditorKeymap Set(EditorCommand command, KeyBinding[]? bindings)
    {
        var updated = new Dictionary<string, KeyBinding[]>(Overrides, StringComparer.Ordinal);
        if (bindings is null) updated.Remove(command.Id.ToString());
        else {
            if (bindings.Length > 8) throw new ArgumentException("Use at most eight shortcuts per command.");
            var normalized = bindings.Select(binding => Validate(command, binding)).ToArray();
            if (normalized.Distinct().Count() != normalized.Length) throw new ArgumentException("This shortcut is already assigned to this command.");
            updated[command.Id.ToString()] = normalized.ToArray();
        }
        var result = Profile == KeyboardProfile.Standard ? this with { Standard = updated } : this with { Vim = updated };
        var compiled = result.Compile();
        foreach (var binding in compiled.Where(binding => binding.Command == command.Id)) {
            var conflict = compiled.FirstOrDefault(other => other.Command != binding.Command && other.When == binding.When
                && (other.Mode is null || binding.Mode is null || other.Mode == binding.Mode)
                && Prefix(binding.Keys, other.Keys));
            if (conflict is not null) throw new ArgumentException($"Shortcut conflicts with {EditorCommand.Get(conflict.Command).Title} in {binding.When}.");
        }
        return result;
    }

    internal string Serialize() => JsonSerializer.Serialize(new Settings(1, Profile, Standard, Vim), JsonOptions);

    internal static EditorKeymap Deserialize(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object) throw new ArgumentException("Keyboard settings must be an object.");
        if (!document.RootElement.TryGetProperty("version", out _))
            return Default.ReadOverrides(json);
        var settings = JsonSerializer.Deserialize<Settings>(json, JsonOptions)
            ?? throw new ArgumentException("Keyboard settings must be an object.");
        if (settings.Version != 1 || !Enum.IsDefined(settings.Profile) || settings.Standard is null || settings.Vim is null)
            throw new ArgumentException("Unsupported keyboard settings.");
        return Default.ReadOverrides(JsonSerializer.Serialize(settings.Standard, JsonOptions))
            .Select(KeyboardProfile.Vim).ReadOverrides(JsonSerializer.Serialize(settings.Vim, JsonOptions)).Select(settings.Profile);
    }

    private EditorKeymap ReadOverrides(string json)
    {
        var overrides = JsonSerializer.Deserialize<Dictionary<string, KeyBinding[]>>(json, JsonOptions)
            ?? throw new ArgumentException("Keyboard settings must be an object.");
        if (overrides.Count > EditorCommand.All.Count) throw new ArgumentException("Too many keyboard commands.");
        var normalized = new Dictionary<string, KeyBinding[]>(StringComparer.Ordinal);
        foreach (var (id, bindings) in overrides) {
            var command = EditorCommand.Find(id) ?? throw new ArgumentException($"Unknown command '{id}'.");
            if (bindings is null || bindings.Length > 8) throw new ArgumentException($"Invalid bindings for '{id}'.");
            normalized.Add(id, bindings.Select(binding => Validate(command, binding)).ToArray());
        }
        var result = Profile == KeyboardProfile.Standard ? this with { Standard = normalized } : this with { Vim = normalized };
        foreach (var command in EditorCommand.All.Where(command => normalized.ContainsKey(command.Id.ToString())))
            result = result.Set(command, normalized[command.Id.ToString()]);
        return result;
    }

    private KeyBinding Validate(EditorCommand command, KeyBinding binding)
    {
        if (binding is null || string.IsNullOrWhiteSpace(binding.Key) || binding.Key.Length > 160)
            throw new ArgumentException("Enter a shortcut, for example Mod+Shift+B or Mod+K Mod+S.");
        var strokes = binding.Strokes;
        if (strokes.Length is < 1 or > 2) throw new ArgumentException("Use one key combination or a two-step chord.");
        var when = binding.When ?? command.Context;
        if (!Enum.IsDefined(when) || !Allows(command.Context, when)) throw new ArgumentException("This command is unavailable in that context.");
        if (binding.Mode is { } mode && (!Enum.IsDefined(mode)
            || Profile == KeyboardProfile.Standard && mode != EditorInputMode.Standard
            || Profile == KeyboardProfile.Vim && mode == EditorInputMode.Standard))
            throw new ArgumentException("This mode is unavailable in that keyboard profile.");
        for (var index = 0; index < strokes.Length; index++) {
            var stroke = strokes[index];
            if (index == 0 && (stroke.Modifiers & ~ShortcutModifiers.Shift) == 0
                && (stroke.Key.Length == 1 || stroke.Key == "Space" || stroke.Key.StartsWith('['))
                && binding.Mode is not (EditorInputMode.Normal or EditorInputMode.Visual))
                throw new ArgumentException("Unmodified text keys are reserved for typing.");
            var logical = stroke.Key.StartsWith("[KEY", StringComparison.Ordinal) && stroke.Key.Length == 6 ? stroke.Key[4..5]
                : stroke.Key.StartsWith("[DIGIT", StringComparison.Ordinal) && stroke.Key.Length == 8 ? stroke.Key[6..7] : stroke.Key;
            if (index == 0 && binding.Mode is EditorInputMode.Normal or EditorInputMode.Visual
                && stroke.Modifiers == ShortcutModifiers.None && logical is [>= '1' and <= '9'])
                throw new ArgumentException("Digits 1–9 start Vim counts. Choose another first key.");
            if (index == 0 && logical is "C" or "V" or "X" && (stroke.Modifiers & (ShortcutModifiers.Primary | ShortcutModifiers.Control | ShortcutModifiers.Meta)) != 0
                && (stroke.Modifiers & (ShortcutModifiers.Alt | ShortcutModifiers.Shift)) == 0)
                throw new ArgumentException("Native clipboard shortcuts are reserved for copy, paste and cut.");
        }
        return new(string.Join(' ', strokes.Select(static stroke => stroke.ToString())), when, binding.Mode);
    }

    internal static bool Allows(CommandContext owner, CommandContext current)
        => owner == CommandContext.Workbench || owner == current || owner == CommandContext.Editor
            && current is CommandContext.EditorLab or CommandContext.Notebook or CommandContext.Suggestions or CommandContext.Signatures;

    private static bool Prefix(KeyStroke[] first, KeyStroke[] second)
        => first.Take(System.Math.Min(first.Length, second.Length)).Zip(second).All(pair => Matches(pair.First, pair.Second));

    internal static bool Matches(KeyStroke first, KeyStroke second)
    {
        static string Logical(string key) => key.StartsWith("[KEY", StringComparison.Ordinal) && key.Length == 6 ? key[4..5]
            : key.StartsWith("[DIGIT", StringComparison.Ordinal) && key.Length == 8 ? key[6..7] : key;
        static IEnumerable<ShortcutModifiers> Expand(ShortcutModifiers modifiers)
            => (modifiers & ShortcutModifiers.Primary) == 0 ? [modifiers]
                : [(modifiers & ~ShortcutModifiers.Primary) | ShortcutModifiers.Control, (modifiers & ~ShortcutModifiers.Primary) | ShortcutModifiers.Meta];
        return Logical(first.Key) == Logical(second.Key) && Expand(first.Modifiers).Intersect(Expand(second.Modifiers)).Any();
    }

    private sealed record Settings(int Version, KeyboardProfile Profile,
        IReadOnlyDictionary<string, KeyBinding[]> Standard, IReadOnlyDictionary<string, KeyBinding[]> Vim);
}
