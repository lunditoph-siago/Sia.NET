namespace Sia_Examples.Notebook;

public enum CellPhase
{
    Idle,
    Compiling,
    CompileError,
    Compiled,
    Running,
    RanSuccess,
    RanError,
    Interrupted,
}

public sealed record CellState(
    CellPhase Phase,
    string Source,
    IReadOnlyList<HighlightRun> Highlights,
    IReadOnlyList<NotebookDiagnostic> Diagnostics,
    string StdOut,
    string StdErr);

public sealed class NotebookSession : IDisposable
{
    private sealed class ScopeState
    {
        public required IReadOnlyList<CodeCellBlock> Cells { get; init; }
        public NotebookCompiler? Compiler;
        public int CompiledThroughIndex = -1;
        public byte[]? CompiledAssembly;
        public NotebookProgram? CompiledProgram;
    }

    private readonly List<CodeCellBlock> _cells;
    private readonly Dictionary<string, string> _scopeKeyById;
    private readonly Dictionary<string, int> _indexInScope;
    private readonly Dictionary<string, ScopeState> _scopes;
    private readonly Dictionary<string, string> _sourceOverrides = [];
    private readonly Dictionary<string, CellState> _states = [];
    private readonly IMetadataReferenceProvider _references;

    private CancellationTokenSource? _cts;
    private bool _running;
    private object _runToken = new();

    public event Action? Changed;

    public NotebookSession(NotebookDocument document, IMetadataReferenceProvider references)
    {
        _references = references;
        _cells = document.Sections
            .SelectMany(s => s.Blocks)
            .OfType<CodeCellBlock>()
            .ToList();

        _scopeKeyById = [];
        var groups = new Dictionary<string, List<CodeCellBlock>>();
        foreach (var cell in _cells) {
            var key = cell.Scope ?? $"$solo:{cell.Id}";
            _scopeKeyById[cell.Id] = key;
            if (!groups.TryGetValue(key, out var list)) {
                groups[key] = list = [];
            }
            list.Add(cell);
        }

        _scopes = groups.ToDictionary(kv => kv.Key, kv => new ScopeState { Cells = kv.Value });

        _indexInScope = [];
        foreach (var list in groups.Values) {
            for (var i = 0; i < list.Count; i++) {
                _indexInScope[list[i].Id] = i;
            }
        }

        foreach (var cell in _cells) {
            _states[cell.Id] = new CellState(CellPhase.Idle, cell.InitialSource, [], [], "", "");
        }
    }

    public IReadOnlyList<CodeCellBlock> Cells => _cells;

    public CellState GetState(string cellId) => _states[cellId];

    public bool IsBusy => _cts is not null || _running;

    public async Task EnsureHighlightsAsync()
    {
        foreach (var cell in _cells) {
            var highlights = await CSharpHighlighter.ClassifyAsync(SourceOf(cell)).ConfigureAwait(false);
            SetState(cell.Id, s => s with { Highlights = highlights });
        }
        Changed?.Invoke();
    }

    public async Task UpdateCellSourceAsync(string cellId, string source)
    {
        if (!_scopeKeyById.TryGetValue(cellId, out var scopeKey)) {
            return;
        }
        _sourceOverrides[cellId] = source;
        var highlights = await CSharpHighlighter.ClassifyAsync(source).ConfigureAwait(false);
        SetState(cellId, s => s with { Source = source, Highlights = highlights });
        InvalidateFrom(_scopes[scopeKey], _indexInScope[cellId]);
        Changed?.Invoke();
    }

    public async Task CompileThroughAsync(string cellId, CancellationToken ct = default)
    {
        if (!_scopeKeyById.TryGetValue(cellId, out var scopeKey) || IsBusy) {
            return;
        }
        var scope = _scopes[scopeKey];
        var targetIndex = _indexInScope[cellId];
        var scopeCells = scope.Cells;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _cts = cts;
        try {
            for (var i = 0; i <= targetIndex; i++) {
                SetPhase(scopeCells[i].Id, CellPhase.Compiling);
            }
            Changed?.Invoke();

            var program = BuildProgram(scopeCells, targetIndex);
            scope.Compiler ??= new NotebookCompiler(program, _references);
            scope.Compiler.UpdateProgram(program);

            NotebookCompileResult result;
            try {
                result = await scope.Compiler.CompileAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) {
                for (var i = 0; i <= targetIndex; i++) {
                    SetPhase(scopeCells[i].Id, CellPhase.Interrupted);
                }
                return;
            }

            var diagnosticsByCell = GroupDiagnosticsByCell(result.Diagnostics, program, scopeCells[targetIndex].Id);

            if (result.Success) {
                scope.CompiledThroughIndex = targetIndex;
                scope.CompiledAssembly = result.AssemblyImage;
                scope.CompiledProgram = program;
                for (var i = 0; i <= targetIndex; i++) {
                    var id = scopeCells[i].Id;
                    SetState(id, s => s with {
                        Phase = CellPhase.Compiled,
                        Diagnostics = diagnosticsByCell.GetValueOrDefault(id, []),
                    });
                }
            }
            else {
                scope.CompiledThroughIndex = -1;
                scope.CompiledAssembly = null;
                scope.CompiledProgram = null;
                for (var i = 0; i <= targetIndex; i++) {
                    var id = scopeCells[i].Id;
                    var diags = diagnosticsByCell.GetValueOrDefault(id, []);
                    var hasError = diags.Any(d => d.Severity == NotebookDiagnosticSeverity.Error);
                    SetState(id, s => s with {
                        Phase = hasError ? CellPhase.CompileError : CellPhase.Idle,
                        Diagnostics = diags,
                    });
                }
            }
        }
        finally {
            _cts = null;
            Changed?.Invoke();
        }
    }

    public async Task RunThroughAsync(string cellId, CancellationToken ct = default)
    {
        if (!_scopeKeyById.TryGetValue(cellId, out var scopeKey) || IsBusy) {
            return;
        }
        var scope = _scopes[scopeKey];
        var targetIndex = _indexInScope[cellId];
        var scopeCells = scope.Cells;

        if (scope.CompiledThroughIndex < targetIndex || scope.CompiledProgram is null || scope.CompiledAssembly is null) {
            await CompileThroughAsync(cellId, ct).ConfigureAwait(false);
            if (scope.CompiledThroughIndex < targetIndex || scope.CompiledAssembly is null || scope.CompiledProgram is null) {
                return;
            }
        }

        var program = scope.CompiledProgram;
        var assembly = scope.CompiledAssembly;

        var myToken = new object();
        _runToken = myToken;
        _running = true;

        for (var i = 0; i <= targetIndex; i++) {
            SetPhase(scopeCells[i].Id, CellPhase.Running);
        }
        for (var i = targetIndex + 1; i < scopeCells.Count; i++) {
            SetPhase(scopeCells[i].Id, CellPhase.Idle);
        }
        Changed?.Invoke();

        var result = await NotebookCompiler.ExecuteAsync(assembly).ConfigureAwait(false);

        _running = false;
        if (ReferenceEquals(_runToken, myToken)) {
            ApplyRunResult(result, program, scopeCells, targetIndex);
        }

        Changed?.Invoke();
    }

    public void Interrupt() => _cts?.Cancel();

    private void ApplyRunResult(
        NotebookExecuteResult result, NotebookProgram program, IReadOnlyList<CodeCellBlock> scopeCells, int targetIndex)
    {
        var stdoutByCell = NotebookProgramBuilder.SliceOutput(result.StdOut, program);
        var stderrByCell = NotebookProgramBuilder.SliceOutput(result.StdErr, program);

        var lastStartedIndex = -1;
        for (var i = 0; i <= targetIndex; i++) {
            if (stdoutByCell.ContainsKey(scopeCells[i].Id)) {
                lastStartedIndex = i;
            }
        }

        for (var i = 0; i <= targetIndex; i++) {
            var id = scopeCells[i].Id;
            if (!stdoutByCell.TryGetValue(id, out var stdout)) {
                continue;
            }
            stderrByCell.TryGetValue(id, out var stderr);
            stderr ??= "";

            var failedHere = !result.Success && i == lastStartedIndex;
            var phase = stderr.Length > 0 || failedHere ? CellPhase.RanError : CellPhase.RanSuccess;

            SetState(id, s => s with { Phase = phase, StdOut = stdout, StdErr = stderr });
        }
    }

    private Dictionary<string, List<NotebookDiagnostic>> GroupDiagnosticsByCell(
        IReadOnlyList<NotebookDiagnostic> diagnostics, NotebookProgram program, string fallbackCellId)
    {
        var byCell = new Dictionary<string, List<NotebookDiagnostic>>();
        foreach (var diagnostic in diagnostics) {
            var owner = diagnostic.InUserCode ? program.ResolveCellId(diagnostic.Line - 1) : null;
            owner ??= fallbackCellId;
            if (!byCell.TryGetValue(owner, out var list)) {
                byCell[owner] = list = [];
            }
            list.Add(diagnostic);
        }
        return byCell;
    }

    private void InvalidateFrom(ScopeState scope, int index)
    {
        for (var i = index; i < scope.Cells.Count; i++) {
            var cell = scope.Cells[i];
            SetState(cell.Id, s => s with { Phase = CellPhase.Idle, Diagnostics = [], StdOut = "", StdErr = "" });
        }
        if (scope.CompiledThroughIndex >= index) {
            scope.CompiledThroughIndex = -1;
            scope.CompiledAssembly = null;
            scope.CompiledProgram = null;
        }
    }

    private string SourceOf(CodeCellBlock cell)
        => _sourceOverrides.TryGetValue(cell.Id, out var source) ? source : cell.InitialSource;

    private NotebookProgram BuildProgram(IReadOnlyList<CodeCellBlock> scopeCells, int throughIndex)
        => NotebookProgramBuilder.Build(
            scopeCells.Take(throughIndex + 1).Select(c => (c.Id, SourceOf(c))).ToList());

    private void SetPhase(string cellId, CellPhase phase)
        => SetState(cellId, s => s with { Phase = phase });

    private void SetState(string cellId, Func<CellState, CellState> update)
        => _states[cellId] = update(_states[cellId]);

    public void Dispose()
    {
        Interrupt();
        foreach (var scope in _scopes.Values) {
            scope.Compiler?.Dispose();
        }
    }
}
