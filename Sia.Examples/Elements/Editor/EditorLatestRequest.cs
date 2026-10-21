namespace Sia.Examples.Editor;

// UI-thread ownership for queries whose newest request replaces all older work.
// Cancellation retires a result immediately; the caller's finally disposes its
// token source only after the asynchronous operation has actually returned.
internal sealed class EditorLatestRequest<T>
{
    private Request? _current;

    public Request Start(T context, CancellationToken lifetime = default)
    {
        Cancel();
        return _current = new(this, context, lifetime);
    }

    public void Cancel()
    {
        var current = _current;
        _current = null;
        current?.Cancel();
    }

    internal sealed class Request : IDisposable
    {
        private readonly EditorLatestRequest<T> _owner;
        private readonly CancellationTokenSource _cancellation;

        internal Request(EditorLatestRequest<T> owner, T context, CancellationToken lifetime)
        {
            _owner = owner;
            Context = context;
            _cancellation = lifetime.CanBeCanceled
                ? CancellationTokenSource.CreateLinkedTokenSource(lifetime) : new();
            Token = _cancellation.Token;
        }

        public T Context { get; }
        public CancellationToken Token { get; }
        public bool IsCurrent => ReferenceEquals(_owner._current, this) && !Token.IsCancellationRequested;

        internal void Cancel() => _cancellation.Cancel();

        public void Dispose()
        {
            if (ReferenceEquals(_owner._current, this)) _owner._current = null;
            _cancellation.Dispose();
        }
    }
}
