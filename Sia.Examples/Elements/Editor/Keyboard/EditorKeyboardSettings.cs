using System.Text.Json;
using System.Text.Json.Serialization;
using Sia.Examples.Dom;

namespace Sia.Examples.Editor;

[JsonConverter(typeof(JsonStringEnumConverter<KeyboardProfile>))]
internal enum KeyboardProfile { Standard, Vim }

[JsonConverter(typeof(JsonStringEnumConverter<EditorInputMode>))]
internal enum EditorInputMode { Standard, Normal, Insert, Visual }

internal sealed class EditorKeyboardSettings : IDisposable
{
    private readonly DomElement _root = DomElement.Find("app");
    internal EditorKeymap Value { get; private set; } = EditorKeymap.Default;
    internal string? LoadError { get; private set; }
    internal event Action<EditorKeymap>? Changed;

    internal EditorKeyboardSettings()
    {
        try {
            if (DomRuntime.Backend.ReadKeymap() is { } json) Value = EditorKeymap.Deserialize(json);
        }
        catch (Exception error) { LoadError = $"Keyboard settings could not be loaded: {error.Message}"; }
        Project();
    }

    internal void Save(EditorKeymap value)
    {
        DomRuntime.Backend.WriteKeymap(value.Serialize());
        Value = value;
        LoadError = null;
        Project();
        Changed?.Invoke(value);
    }

    private void Project() => _root.Attr("data-editor-keymap", JsonSerializer.Serialize(Value.Compile(), JsonSerializerOptions.Web));

    public void Dispose()
    {
        Changed = null;
        _root.Attr("data-editor-keymap", "");
        _root.Dispose();
    }
}
