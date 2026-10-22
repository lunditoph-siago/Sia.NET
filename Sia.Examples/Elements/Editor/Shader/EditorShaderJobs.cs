namespace Sia.Examples.Editor;

internal enum ShaderAction { Compile, Run }

internal sealed record ShaderRequest(ShaderAction Action, string Folder, string? EntryName);

// UI-thread coordination only. Cancelling invalidates results immediately, but the
// active job keeps its token alive until its compiler phase actually returns.
internal sealed class EditorShaderJobs
{
    private EditorShaderJob? _active;
    private ShaderRequest? _pending;

    public int Revision { get; private set; }
    public bool IsRunning => _active is not null;
    public bool IsCancellationRequested => _active?.Token.IsCancellationRequested == true;
    public bool IsCurrent(int revision) => revision == Revision;

    public EditorShaderJob Start(ShaderRequest request, CancellationToken lifetime)
    {
        if (IsRunning) throw new InvalidOperationException("A shader job is already running.");
        return _active = new(request, ++Revision, lifetime);
    }

    public void Queue(ShaderRequest request)
    {
        if (!IsRunning) throw new InvalidOperationException("No shader job is running.");
        _pending = request;
        Cancel(clearPending: false);
    }

    public void Cancel(bool clearPending = true)
    {
        ++Revision;
        if (clearPending) _pending = null;
        _active?.Cancellation.Cancel();
    }

    public ShaderRequest? Complete(EditorShaderJob job)
    {
        if (!ReferenceEquals(job, _active))
            throw new InvalidOperationException("Only the active shader job can complete.");
        var next = job.Lifetime.IsCancellationRequested ? null : _pending;
        _active = null;
        _pending = null;
        job.Cancellation.Dispose();
        return next;
    }
}

internal sealed class EditorShaderJob(ShaderRequest request, int revision, CancellationToken lifetime)
{
    internal CancellationTokenSource Cancellation { get; } = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
    public ShaderRequest Request { get; } = request;
    public int Revision { get; } = revision;
    public CancellationToken Lifetime { get; } = lifetime;
    public CancellationToken Token => Cancellation.Token;
}
