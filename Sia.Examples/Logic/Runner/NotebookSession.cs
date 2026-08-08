using System.Collections.Immutable;
using Sia_Examples.Browser;

namespace Sia_Examples.Notebook;

public sealed class NotebookSession : IDisposable
{
    private readonly BrowserMainThread _mainThread;
    private readonly List<CodeCellBlock> _cells;
    private readonly Dictionary<string, string> _scopeKeys;
    private readonly Dictionary<string, int> _scopeIndices;
    private readonly Dictionary<string, ScopeState> _scopes;
    private readonly Dictionary<string, CellState> _states = [];
    private readonly MetadataReferenceProvider _references;
    private readonly PackageRegistry _packages = new();

    private ImmutableArray<NotebookCellSnapshot> _cellSnapshot = [];
    private CancellationTokenSource? _operationCancellation;
    private ActiveRun? _activeRun;
    private int _version;
    private bool _cellsDirty = true;
    private bool _disposed;

    public NotebookSession(
        BrowserMainThread mainThread,
        NotebookDocument document,
        MetadataReferenceProvider references)
    {
        _mainThread = mainThread;
        _references = references;
        _cells = document.Sections
            .SelectMany(static section => section.Blocks)
            .OfType<CodeCellBlock>()
            .ToList();
        _scopeKeys = [];
        _scopeIndices = [];

        foreach (var package in document.Packages) {
            _packages.Declare(package);
        }

        var groupedCells = new Dictionary<string, List<CodeCellBlock>>();
        foreach (var cell in _cells) {
            var scopeKey = cell.Scope ?? $"$cell:{cell.Id}";
            _scopeKeys.Add(cell.Id, scopeKey);
            if (!groupedCells.TryGetValue(scopeKey, out var scopeCells)) {
                scopeCells = [];
                groupedCells.Add(scopeKey, scopeCells);
            }
            _scopeIndices.Add(cell.Id, scopeCells.Count);
            scopeCells.Add(cell);
            _states.Add(cell.Id, CellState.Create(cell.InitialSource) with {
                Highlights = CSharpHighlighter
                    .Classify(cell.InitialSource)
                    .ToImmutableArray(),
            });
        }

        _scopes = groupedCells.ToDictionary(
            static pair => pair.Key,
            static pair => new ScopeState(pair.Value));
        Snapshot = CreateSnapshot();
    }

    public event Action<NotebookSessionSnapshot>? Changed;

    public IReadOnlyList<CodeCellBlock> Cells => _cells;

    public NotebookSessionSnapshot Snapshot { get; private set; }

    public CellState GetState(string cellId) => _states[cellId];

    public bool IsBusy => _operationCancellation is not null || _activeRun is not null;

    public async Task EnsurePackagesAsync(CancellationToken cancellationToken = default)
    {
        VerifyAccess();
        var pending = _packages.Snapshot
            .Where(static status => status.State == PackageLoadState.Loading)
            .Select(static status => status.Package)
            .ToArray();

        foreach (var package in pending) {
            await LoadPackageAsync(package, cancellationToken);
        }
    }

    public async Task<PackageStatus> AddPackageAsync(
        PackageRef package,
        CancellationToken cancellationToken = default)
    {
        VerifyAccess();
        if (_packages.Declare(package)) {
            Publish();
        }
        return await LoadPackageAsync(package, cancellationToken);
    }

    public void UpdateCellSource(
        string cellId,
        string source,
        CancellationToken cancellationToken = default)
    {
        VerifyAccess();
        if (!_scopeKeys.TryGetValue(cellId, out var scopeKey)
            || _states[cellId].Source == source) {
            return;
        }

        var highlights = CSharpHighlighter.Classify(source);
        cancellationToken.ThrowIfCancellationRequested();

        SetState(cellId, state => state with {
            Source = source,
            Highlights = highlights.ToImmutableArray(),
        });
        InvalidateFrom(_scopes[scopeKey], _scopeIndices[cellId]);
        Publish();
    }

    public async Task CompileThroughAsync(
        string cellId,
        CancellationToken cancellationToken = default)
    {
        VerifyAccess();
        if (!TryGetTarget(cellId, out var scope, out var targetIndex) || IsBusy) {
            return;
        }
        if (scope.HasCompilation(targetIndex)) {
            SetPhases(scope.Cells, targetIndex, CellPhase.Compiled);
            Publish();
            return;
        }

        using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _operationCancellation = operation;
        try {
            SetPhases(scope.Cells, targetIndex, CellPhase.Compiling);
            Publish();
            await CompileCoreAsync(scope, targetIndex, operation.Token);
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested) {
            SetPhases(scope.Cells, targetIndex, CellPhase.Interrupted);
        } finally {
            VerifyAccess();
            _operationCancellation = null;
            Publish();
        }
    }

    public async Task RunThroughAsync(
        string cellId,
        CancellationToken cancellationToken = default)
    {
        VerifyAccess();
        if (!TryGetTarget(cellId, out var scope, out var targetIndex) || IsBusy) {
            return;
        }

        using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _operationCancellation = operation;
        try {
            if (!scope.HasCompilation(targetIndex)) {
                SetPhases(scope.Cells, targetIndex, CellPhase.Compiling);
                Publish();
                if (!await CompileCoreAsync(scope, targetIndex, operation.Token)) {
                    return;
                }
            }

            var compilation = scope.GetCompilation(targetIndex);
            var run = new ActiveRun(scope.Cells, targetIndex);
            _activeRun = run;
            SetPhases(scope.Cells, targetIndex, CellPhase.Running);
            ResetFollowingCells(scope.Cells, targetIndex);
            Publish();

            var result = await NotebookCompiler.ExecuteAsync(compilation.Assembly);
            VerifyAccess();
            if (ReferenceEquals(_activeRun, run)) {
                ApplyRunResult(result, compilation.Program, scope.Cells, targetIndex);
            }
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested) {
            SetPhases(scope.Cells, targetIndex, CellPhase.Interrupted);
        } finally {
            VerifyAccess();
            _activeRun = null;
            _operationCancellation = null;
            Publish();
        }
    }

    public void Interrupt()
    {
        VerifyAccess();
        _operationCancellation?.Cancel();
        if (_activeRun is not { } run) {
            return;
        }

        _activeRun = null;
        SetPhases(run.Cells, run.TargetIndex, CellPhase.Interrupted);
        Publish();
    }

    public void Dispose()
    {
        if (_disposed) {
            return;
        }
        VerifyAccess();
        _disposed = true;
        _operationCancellation?.Cancel();
        _operationCancellation = null;
        _activeRun = null;
        Changed = null;
    }

    private async Task<PackageStatus> LoadPackageAsync(
        PackageRef package,
        CancellationToken cancellationToken)
    {
        PackageStatus status;
        try {
            await _references.EnsurePackagesAsync([package], cancellationToken);
            VerifyAccess();
            status = new(package, PackageLoadState.Loaded, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            throw;
        }
        catch (Exception error) {
            VerifyAccess();
            status = new(package, PackageLoadState.Failed, error.Message);
        }

        if (_packages.Resolve(status)) {
            Publish();
        }
        return status;
    }

    private async Task<bool> CompileCoreAsync(
        ScopeState scope,
        int targetIndex,
        CancellationToken cancellationToken)
    {
        var program = BuildProgram(scope.Cells, targetIndex);
        scope.SetProgram(program, _references);
        var result = await scope.Compiler!.CompileAsync(cancellationToken);
        VerifyAccess();

        var diagnostics = GroupDiagnosticsByCell(
            result.Diagnostics,
            program,
            scope.Cells[targetIndex].Id);
        if (!result.Success) {
            scope.RemoveCompilation(targetIndex);
            for (var index = 0; index <= targetIndex; index++) {
                var id = scope.Cells[index].Id;
                var cellDiagnostics = diagnostics.GetValueOrDefault(id, []);
                SetState(id, state => state with {
                    Phase = cellDiagnostics.Any(static diagnostic =>
                        diagnostic.Severity == NotebookDiagnosticSeverity.Error)
                            ? CellPhase.CompileError
                            : CellPhase.Idle,
                    Diagnostics = cellDiagnostics.ToImmutableArray(),
                });
            }
            return false;
        }

        scope.CommitCompilation(targetIndex, program, result.AssemblyImage!);
        for (var index = 0; index <= targetIndex; index++) {
            var id = scope.Cells[index].Id;
            SetState(id, state => state with {
                Phase = CellPhase.Compiled,
                Diagnostics = diagnostics.GetValueOrDefault(id, []).ToImmutableArray(),
            });
        }
        return true;
    }

    private void ApplyRunResult(
        NotebookExecuteResult result,
        NotebookProgram program,
        IReadOnlyList<CodeCellBlock> scopeCells,
        int targetIndex)
    {
        var standardOutput = NotebookProgramBuilder.SliceOutput(result.StdOut, program);
        var standardError = NotebookProgramBuilder.SliceOutput(result.StdErr, program);
        var lastStartedIndex = -1;
        for (var index = 0; index <= targetIndex; index++) {
            if (standardOutput.ContainsKey(scopeCells[index].Id)) {
                lastStartedIndex = index;
            }
        }

        for (var index = 0; index <= targetIndex; index++) {
            var id = scopeCells[index].Id;
            if (!standardOutput.TryGetValue(id, out var output)) {
                continue;
            }
            var error = standardError.GetValueOrDefault(id, string.Empty);
            var failed = error.Length > 0 || (!result.Success && index == lastStartedIndex);
            SetState(id, state => state with {
                Phase = failed ? CellPhase.RanError : CellPhase.RanSuccess,
                StandardOutput = output,
                StandardError = error,
            });
        }
    }

    private static Dictionary<string, List<NotebookDiagnostic>> GroupDiagnosticsByCell(
        IReadOnlyList<NotebookDiagnostic> diagnostics,
        NotebookProgram program,
        string fallbackCellId)
    {
        var result = new Dictionary<string, List<NotebookDiagnostic>>();
        foreach (var diagnostic in diagnostics) {
            var cellId = diagnostic.InUserCode
                ? program.ResolveCellId(diagnostic.Line - 1)
                : null;
            cellId ??= fallbackCellId;
            if (!result.TryGetValue(cellId, out var cellDiagnostics)) {
                cellDiagnostics = [];
                result.Add(cellId, cellDiagnostics);
            }
            cellDiagnostics.Add(diagnostic);
        }
        return result;
    }

    private void InvalidateFrom(ScopeState scope, int startIndex)
    {
        for (var index = startIndex; index < scope.Cells.Count; index++) {
            var id = scope.Cells[index].Id;
            SetState(id, state => state with {
                Phase = CellPhase.Idle,
                Diagnostics = [],
                StandardOutput = string.Empty,
                StandardError = string.Empty,
            });
        }
        scope.InvalidateFrom(startIndex);
    }

    private bool TryGetTarget(
        string cellId,
        out ScopeState scope,
        out int targetIndex)
    {
        if (_scopeKeys.TryGetValue(cellId, out var scopeKey)) {
            scope = _scopes[scopeKey];
            targetIndex = _scopeIndices[cellId];
            return true;
        }

        scope = null!;
        targetIndex = -1;
        return false;
    }

    private NotebookProgram BuildProgram(
        IReadOnlyList<CodeCellBlock> scopeCells,
        int targetIndex)
        => NotebookProgramBuilder.Build(scopeCells
            .Take(targetIndex + 1)
            .Select(cell => (cell.Id, _states[cell.Id].Source))
            .ToArray());

    private void SetPhases(
        IReadOnlyList<CodeCellBlock> cells,
        int targetIndex,
        CellPhase phase)
    {
        for (var index = 0; index <= targetIndex; index++) {
            SetState(cells[index].Id, state => state with { Phase = phase });
        }
    }

    private void ResetFollowingCells(IReadOnlyList<CodeCellBlock> cells, int targetIndex)
    {
        for (var index = targetIndex + 1; index < cells.Count; index++) {
            SetState(cells[index].Id, state => state with { Phase = CellPhase.Idle });
        }
    }

    private void SetState(string cellId, Func<CellState, CellState> update)
    {
        var current = _states[cellId];
        var next = update(current);
        if (next != current) {
            _states[cellId] = next;
            _cellsDirty = true;
        }
    }

    private void Publish()
    {
        VerifyAccess();
        Snapshot = CreateSnapshot();
        Changed?.Invoke(Snapshot);
    }

    private NotebookSessionSnapshot CreateSnapshot()
    {
        if (_cellsDirty) {
            _cellSnapshot = [.. _cells.Select(cell =>
                new NotebookCellSnapshot(cell.Id, _states[cell.Id]))];
            _cellsDirty = false;
        }
        return new(++_version, _cellSnapshot, _packages.Snapshot);
    }

    private void VerifyAccess()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _mainThread.VerifyAccess();
    }

    private sealed class ScopeState(IReadOnlyList<CodeCellBlock> cells)
    {
        private readonly Dictionary<int, CompilationArtifact> _compilations = [];

        public IReadOnlyList<CodeCellBlock> Cells { get; } = cells;

        public NotebookCompiler? Compiler { get; private set; }

        public bool HasCompilation(int targetIndex)
            => _compilations.ContainsKey(targetIndex);

        public CompilationArtifact GetCompilation(int targetIndex)
            => _compilations[targetIndex];

        public void SetProgram(
            NotebookProgram program,
            ICompilationReferenceResolver references)
        {
            if (Compiler is null) {
                Compiler = new(program, references);
            } else {
                Compiler.UpdateProgram(program);
            }
        }

        public void CommitCompilation(
            int targetIndex,
            NotebookProgram program,
            byte[] assembly)
            => _compilations[targetIndex] = new(program, assembly);

        public void RemoveCompilation(int targetIndex)
            => _compilations.Remove(targetIndex);

        public void InvalidateFrom(int startIndex)
        {
            foreach (var targetIndex in _compilations.Keys.ToArray()) {
                if (targetIndex >= startIndex) {
                    _compilations.Remove(targetIndex);
                }
            }
        }

        public readonly record struct CompilationArtifact(
            NotebookProgram Program,
            byte[] Assembly);
    }

    private sealed record ActiveRun(IReadOnlyList<CodeCellBlock> Cells, int TargetIndex);
}
