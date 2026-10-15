using System.Text;

namespace Sia_Examples.Notebook;

internal sealed class ProgramConsole(int capacity = 65536) : IDisposable
{
    private static readonly AsyncLocal<ProgramConsole?> Current = new();
    private readonly StringBuilder _output = new();
    private readonly StringBuilder _error = new();
    private readonly StringBuilder _text = new();
    private readonly object _gate = new();
    private bool _disposed;
    private int _revision;

    static ProgramConsole()
    {
        Console.SetOut(new Router(Console.Out, false));
        Console.SetError(new Router(Console.Error, true));
    }

    public IDisposable Capture()
    {
        var previous = Current.Value;
        Current.Value = this;
        return new Scope(previous);
    }

    public (int Revision, string Output, string Error, string Text) Snapshot(int previousRevision = -1)
    {
        lock (_gate) return previousRevision == _revision ? (_revision, "", "", "")
            : (_revision, _output.ToString(), _error.ToString(), _text.ToString());
    }

    public void Clear()
    {
        lock (_gate) { _output.Clear(); _error.Clear(); _text.Clear(); _revision++; }
    }

    public void Dispose() { lock (_gate) _disposed = true; }

    private void Write(string? text, bool error)
    {
        if (text is null) return;
        lock (_gate) {
            if (_disposed) return;
            var target = error ? _error : _output;
            target.Append(text);
            _text.Append(text);
            if (target.Length > capacity) target.Remove(0, target.Length - capacity);
            if (_text.Length > capacity) _text.Remove(0, _text.Length - capacity);
            _revision++;
        }
    }

    private sealed class Scope(ProgramConsole? previous) : IDisposable
    {
        public void Dispose() => Current.Value = previous;
    }

    private sealed class Router(TextWriter fallback, bool error) : TextWriter
    {
        public override Encoding Encoding => fallback.Encoding;
        public override void Write(char value) => Write(value.ToString());
        public override void Write(string? value)
        {
            if (Current.Value is { } owner) owner.Write(value, error);
            else fallback.Write(value);
        }
        public override void WriteLine(string? value) => Write(value + NewLine);
    }
}
