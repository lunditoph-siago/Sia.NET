using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace Sia.Examples.Editor;

// Wire values match inputKinds in pipeline-canvas.js.
internal enum EditorPipelineInputKind
{
    MouseMoved = 0, MouseScrolled = 1, MouseEntered = 2, MouseExited = 3,
    MouseButtonPressed = 4, MouseButtonReleased = 5,
    KeyPressed = 6, KeyRepeated = 7, KeyReleased = 8, TextEntered = 9,
}

internal readonly record struct EditorPipelineInput(EditorPipelineInputKind Kind,
    double X, double Y, int Code, int Modifiers);

// The browser supplies one frame header followed by five numbers per input event.
// Keep input until a frame executes, including adapter setup, pause and readback waits.
internal sealed class EditorPipelineFrames
{
    private readonly List<EditorPipelineInput> _inputs = [];
    private byte[] _packetBytes = new byte[256];

    public double Time { get; private set; }
    public double CssWidth { get; private set; }
    public double CssHeight { get; private set; }
    public double Scale { get; private set; }
    public ReadOnlySpan<EditorPipelineInput> Inputs => CollectionsMarshal.AsSpan(_inputs);

    public void Receive(string packet)
    {
        // Reuse encoding storage instead of allocating a double[] for every frame.
        var required = Encoding.UTF8.GetMaxByteCount(packet.Length);
        if (required > _packetBytes.Length)
            Array.Resize(ref _packetBytes, System.Math.Max(required, _packetBytes.Length * 2));
        var length = Encoding.UTF8.GetBytes(packet, _packetBytes);
        var reader = new Utf8JsonReader(_packetBytes.AsSpan(0, length));
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartArray)
            throw new InvalidDataException("Invalid pipeline frame packet.");
        var time = ReadNumber(ref reader);
        var width = ReadNumber(ref reader);
        var height = ReadNumber(ref reader);
        var scale = ReadNumber(ref reader);
        var previousCount = _inputs.Count;
        try {
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray) {
                if (reader.TokenType != JsonTokenType.Number)
                    throw new InvalidDataException("Invalid pipeline input event.");
                var kind = (EditorPipelineInputKind)reader.GetDouble();
                var x = ReadNumber(ref reader);
                var y = ReadNumber(ref reader);
                var code = (int)ReadNumber(ref reader);
                var modifiers = (int)ReadNumber(ref reader);
                _inputs.Add(new(kind, x, y, code, modifiers));
            }
            if (reader.TokenType != JsonTokenType.EndArray || reader.Read())
                throw new InvalidDataException("Invalid pipeline frame packet.");
        }
        catch {
            _inputs.RemoveRange(previousCount, _inputs.Count - previousCount);
            throw;
        }
        Time = time;
        CssWidth = width;
        CssHeight = height;
        Scale = scale;
    }

    private static double ReadNumber(ref Utf8JsonReader reader)
        => reader.Read() && reader.TokenType == JsonTokenType.Number
            ? reader.GetDouble() : throw new InvalidDataException("Invalid pipeline frame packet.");

    public void ClearInputs() => _inputs.Clear();
}
