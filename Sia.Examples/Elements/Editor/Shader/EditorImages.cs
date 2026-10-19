using System.Text.Json;
using Sia.Reactive;
#if BROWSER
using System.Runtime.InteropServices.JavaScript;
#endif

namespace Sia.Examples.Editor;

public sealed class EditorImage
{
    public string Name { get; }
    public int Width { get; }
    public int Height { get; }
    public int BytesPerRow => Width * 4;
    public ReadOnlyMemory<byte> Pixels { get; }

    internal EditorImage(string name, int width, int height, byte[] pixels)
    {
        if (width is < 1 or > 4096 || height is < 1 or > 4096 || (long)width * height > 4 * 1024 * 1024
            || pixels.Length != (long)width * height * 4)
            throw new InvalidDataException("Invalid RGBA8 image dimensions or pixel count.");
        Name = name;
        Width = width;
        Height = height;
        Pixels = pixels;
    }
}

public static class EditorImageHooks
{
    private readonly record struct Dependencies(EditorUniformSession Session, string Name, State<EditorImageSelection> State);

    public static EditorImage? UseImage(this ref Hooks hooks, in EditorPipelineFrame frame, string name)
    {
        var session = frame.Uniforms ?? throw new InvalidOperationException("Images require an editor pipeline session.");
        var state = hooks.UseState(default(EditorImageSelection));
        hooks.UseEffect(new Dependencies(session, name, state),
            static (in Dependencies d) => d.Session.RegisterImage(d.Name, d.State),
            static (in IDisposable registration) => registration.Dispose());
        return state.Value.Image;
    }
}

internal readonly record struct EditorImageSelection(EditorImage? Image);

internal sealed partial class EditorUniformSession
{
    private readonly Dictionary<string, State<EditorImageSelection>> _images = [];

    public IDisposable RegisterImage(string name, State<EditorImageSelection> state)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var key = "image/" + name;
        if (!_images.TryAdd(key, state)) throw new InvalidOperationException($"Image '{name}' is already registered.");
        _dirty = true;
        return new ImageRegistration(this, key);
    }

    private IEnumerable<object> ImageFields() => _images.Keys.Select(key => new {
        key, name = "Images." + key[6..], type = "image",
    });

    private void ApplyImageEdits()
    {
#if BROWSER
        if (_images.Count == 0) return;
        var json = ReadImages(_binding);
        if (json.Length == 0) return;
        foreach (var edit in JsonSerializer.Deserialize<ImageEdit[]>(json, s_JsonOptions)!) {
            if (!_images.TryGetValue(edit.Key, out var state)) continue;
            state.Set(new(edit.Name is null ? null : new EditorImage(edit.Name, edit.Width, edit.Height, ReadPixels(_binding, edit.Key))));
        }
#endif
    }

    private readonly record struct ImageEdit(string Key, string? Name, int Width, int Height);

    private sealed class ImageRegistration(EditorUniformSession session, string key) : IDisposable
    {
        public void Dispose()
        {
            session._images.Remove(key);
            session._dirty = true;
        }
    }

#if BROWSER
    [JSImport("readUniformImageEdits", "main.js")]
    private static partial string ReadImages(JSObject binding);
    [JSImport("readUniformImagePixels", "main.js")]
    [return: JSMarshalAs<JSType.Array<JSType.Number>>]
    private static partial byte[] ReadPixels(JSObject binding, string key);
#endif
}
