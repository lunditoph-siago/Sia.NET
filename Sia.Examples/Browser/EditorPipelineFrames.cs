using System.Runtime.InteropServices;
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
    private const int HeaderLength = 4;
    private const int InputLength = 5;
    private readonly List<EditorPipelineInput> _inputs = [];

    public double Time { get; private set; }
    public double CssWidth { get; private set; }
    public double CssHeight { get; private set; }
    public double Scale { get; private set; }
    public ReadOnlySpan<EditorPipelineInput> Inputs => CollectionsMarshal.AsSpan(_inputs);

    public void Receive(string packet)
    {
        var frame = JsonSerializer.Deserialize<double[]>(packet);
        if (frame is null || frame.Length < HeaderLength || (frame.Length - HeaderLength) % InputLength != 0)
            throw new InvalidDataException("Invalid pipeline frame packet.");
        Time = frame[0];
        CssWidth = frame[1];
        CssHeight = frame[2];
        Scale = frame[3];
        for (var i = HeaderLength; i < frame.Length; i += InputLength)
            _inputs.Add(new((EditorPipelineInputKind)frame[i], frame[i + 1], frame[i + 2],
                (int)frame[i + 3], (int)frame[i + 4]));
    }

    public void ClearInputs() => _inputs.Clear();
}
