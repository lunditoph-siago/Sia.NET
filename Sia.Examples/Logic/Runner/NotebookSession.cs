using System.Collections.Immutable;

namespace Sia_Examples.Notebook;

public sealed class NotebookSession : IDisposable
{
    private readonly IUiThread _mainThread;
    private readonly MetadataReferenceProvider _references;
    private readonly PackageRegistry _packages = new();

    private NotebookDocument _document;
    private List<CodeCellBlock> _cells = [];
    private Dictionary<string, string> _scopeKeys = [];
    private Dictionary<string, int> _scopeIndices = [];
    private Dictionary<string, ScopeState> _scopes = [];
    private Dictionary<string, CellState> _states = [];

    private ImmutableArray<NotebookCellSnapshot> _cellSnapshot = [];
    private CancellationTokenSource? _operationCancellation;
    private ActiveRun? _activeRun;
    private int _version;
    private bool _cellsDirty = true;
    private bool _disposed;

    public NotebookSession(
        IUiThread mainThread,
        NotebookDocument document,
        MetadataReferenceProvider references)
    {
        _mainThread = mainThread;
        _references = references;
        _document = document;

        foreach (var package in document.Packages) {
            _packages.Declare(package);
        }

        Rebuild();
        Snapshot = CreateSnapshot();
    }

    public event Action<NotebookSessionSnapshot, bool>? Changed;

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
        if (pending.Length == 0) {
            return;
        }

        var statuses = await _references.EnsurePackagesAsync(pending, cancellationToken);
        VerifyAccess();
        ApplyStatuses(statuses);
    }

    public async Task<PackageStatus> AddPackageAsync(
        PackageRef package,
        CancellationToken cancellationToken = default)
    {
        VerifyAccess();
        if (_packages.Declare(package)) {
            Publish();
        }
        var statuses = await _references.EnsurePackagesAsync([package], cancellationToken);
        VerifyAccess();
        ApplyStatuses(statuses);
        return statuses[0];
    }

    private void ApplyStatuses(IReadOnlyList<PackageStatus> statuses)
    {
        var changed = false;
        foreach (var status in statuses) {
            changed |= _packages.Resolve(status);
        }
        if (changed) {
            Publish();
        }
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

    public string InsertCell(string? afterBlockId, string? scope = null)
    {
        VerifyAccess();
        var newCell = new CodeCellBlock(Guid.NewGuid().ToString("N"), "", Editable: true, scope);
        _document = _document with { Sections = InsertBlockAfter(_document.Sections, afterBlockId, newCell) };
        Rebuild();
        Publish(structural: true);
        return newCell.Id;
    }

    public string InsertParagraph(string? afterBlockId)
    {
        VerifyAccess();
        var newParagraph = new ParagraphBlock(Guid.NewGuid().ToString("N"), [new TextInline("")], Editable: true);
        _document = _document with { Sections = InsertBlockAfter(_document.Sections, afterBlockId, newParagraph) };
        Rebuild();
        Publish(structural: true);
        return newParagraph.Id;
    }

    public void SetParagraphText(string blockId, string text)
    {
        VerifyAccess();
        _document = _document with {
            Sections = UpdateBlock(_document.Sections, blockId, block =>
                block is ParagraphBlock { Editable: true } paragraph
                    ? paragraph with { Inlines = [new TextInline(text)] }
                    : block),
        };
        Publish();
    }

    public void RemoveCell(string cellId)
    {
        VerifyAccess();
        _document = _document with { Sections = RemoveBlock(_document.Sections, cellId) };
        Rebuild();
        Publish(structural: true);
    }

    public void MoveCell(string cellId, int offset)
    {
        VerifyAccess();
        if (offset is not (-1 or 1)) {
            throw new ArgumentOutOfRangeException(nameof(offset), offset, "MoveCell only supports -1 (up) or +1 (down).");
        }
        _document = _document with { Sections = MoveBlock(_document.Sections, cellId, offset) };
        Rebuild();
        Publish(structural: true);
    }

    public string InsertSection(
        int? afterIndex, string title, NotebookBlockKind starterKind = NotebookBlockKind.Code)
    {
        VerifyAccess();
        NotebookBlock starterBlock = starterKind == NotebookBlockKind.Text
            ? new ParagraphBlock(Guid.NewGuid().ToString("N"), [new TextInline("")], Editable: true)
            : new CodeCellBlock(Guid.NewGuid().ToString("N"), "", Editable: true, null);
        var sections = _document.Sections.ToList();
        var insertAt = afterIndex is { } index ? Math.Clamp(index + 1, 0, sections.Count) : sections.Count;
        sections.Insert(insertAt, new NotebookSection(Guid.NewGuid().ToString("N"), title, [starterBlock]));
        _document = _document with { Sections = sections };
        Rebuild();
        Publish(structural: true);
        return GetBlockId(starterBlock)!;
    }

    public void RemoveSection(string sectionId)
    {
        VerifyAccess();
        var sections = _document.Sections.ToList();
        var sectionIndex = sections.FindIndex(section => section.Id == sectionId);
        if (sectionIndex < 0) {
            return;
        }
        sections.RemoveAt(sectionIndex);
        _document = _document with { Sections = sections };
        Rebuild();
        Publish(structural: true);
    }

    public void RenameSection(string sectionId, string title)
    {
        VerifyAccess();
        var sections = _document.Sections.ToList();
        var sectionIndex = sections.FindIndex(section => section.Id == sectionId);
        if (sectionIndex < 0) {
            return;
        }
        sections[sectionIndex] = sections[sectionIndex] with { Title = title };
        _document = _document with { Sections = sections };
        Publish();
    }

    public void SetTitle(string title)
    {
        VerifyAccess();
        _document = _document with { Title = title };
        Publish();
    }

    public NotebookDocument ToDocument()
    {
        VerifyAccess();
        var sections = _document.Sections.Select(section => section with {
            Blocks = section.Blocks.Select(block => block switch {
                CodeCellBlock cell => cell with { InitialSource = _states[cell.Id].Source },
                var other => other,
            }).ToList(),
        }).ToList();
        return _document with { Sections = sections };
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
            var rendered = NotebookProgramBuilder.SplitRenderOutput(output);
            var failed = error.Length > 0 || (!result.Success && index == lastStartedIndex);
            SetState(id, state => state with {
                Phase = failed ? CellPhase.RanError : CellPhase.RanSuccess,
                StandardOutput = rendered.StandardOutput,
                StandardError = error,
                RenderRequested = rendered.RenderRequested,
                RenderOutput = rendered.RenderOutput,
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
                RenderRequested = false,
                RenderOutput = string.Empty,
            });
        }
        scope.InvalidateFrom(startIndex);
    }

    private void Rebuild()
    {
        var cells = _document.Sections
            .SelectMany(static section => section.Blocks)
            .OfType<CodeCellBlock>()
            .ToList();

        var scopeKeys = new Dictionary<string, string>();
        var scopeIndices = new Dictionary<string, int>();
        var groupedCells = new Dictionary<string, List<CodeCellBlock>>();
        foreach (var cell in cells) {
            var scopeKey = cell.Scope ?? $"$cell:{cell.Id}";
            scopeKeys.Add(cell.Id, scopeKey);
            if (!groupedCells.TryGetValue(scopeKey, out var scopeCells)) {
                scopeCells = [];
                groupedCells.Add(scopeKey, scopeCells);
            }
            scopeIndices.Add(cell.Id, scopeCells.Count);
            scopeCells.Add(cell);
        }

        var changedScopeKeys = new HashSet<string>();
        foreach (var (scopeKey, scopeCells) in groupedCells) {
            if (!_scopes.TryGetValue(scopeKey, out var previousScope)
                || !scopeCells.Select(static cell => cell.Id).SequenceEqual(
                    previousScope.Cells.Select(static cell => cell.Id))) {
                changedScopeKeys.Add(scopeKey);
            }
        }

        var states = new Dictionary<string, CellState>();
        foreach (var cell in cells) {
            if (!_states.TryGetValue(cell.Id, out var existing)) {
                states[cell.Id] = CellState.Create(cell.InitialSource) with {
                    Highlights = CSharpHighlighter.Classify(cell.InitialSource).ToImmutableArray(),
                };
            } else if (changedScopeKeys.Contains(scopeKeys[cell.Id])) {
                states[cell.Id] = existing with {
                    Phase = CellPhase.Idle,
                    Diagnostics = [],
                    StandardOutput = "",
                    StandardError = "",
                    RenderRequested = false,
                    RenderOutput = "",
                };
            } else {
                states[cell.Id] = existing;
            }
        }

        _cells = cells;
        _scopeKeys = scopeKeys;
        _scopeIndices = scopeIndices;
        _scopes = groupedCells.ToDictionary(
            static pair => pair.Key,
            pair => !changedScopeKeys.Contains(pair.Key) && _scopes.TryGetValue(pair.Key, out var existingScope)
                ? existingScope
                : new ScopeState(pair.Value));
        _states = states;
        _cellsDirty = true;
    }

    private static IReadOnlyList<NotebookSection> InsertBlockAfter(
        IReadOnlyList<NotebookSection> sections, string? afterCellId, NotebookBlock newBlock)
    {
        if (afterCellId is null) {
            if (sections.Count == 0) {
                throw new InvalidOperationException("Cannot insert a cell into a notebook with no sections.");
            }
            var lastIndex = sections.Count - 1;
            var updatedLast = sections.ToList();
            updatedLast[lastIndex] = sections[lastIndex] with {
                Blocks = [.. sections[lastIndex].Blocks, newBlock],
            };
            return updatedLast;
        }

        for (var sectionIndex = 0; sectionIndex < sections.Count; sectionIndex++) {
            var blockIndex = FindBlockIndex(sections[sectionIndex].Blocks, afterCellId);
            if (blockIndex < 0) {
                continue;
            }
            var updatedBlocks = sections[sectionIndex].Blocks.ToList();
            updatedBlocks.Insert(blockIndex + 1, newBlock);
            var updated = sections.ToList();
            updated[sectionIndex] = sections[sectionIndex] with { Blocks = updatedBlocks };
            return updated;
        }

        throw new ArgumentException($"No block with id '{afterCellId}' found.", nameof(afterCellId));
    }

    private static IReadOnlyList<NotebookSection> RemoveBlock(
        IReadOnlyList<NotebookSection> sections, string cellId)
    {
        for (var sectionIndex = 0; sectionIndex < sections.Count; sectionIndex++) {
            var blockIndex = FindBlockIndex(sections[sectionIndex].Blocks, cellId);
            if (blockIndex < 0) {
                continue;
            }
            var updatedBlocks = sections[sectionIndex].Blocks.ToList();
            updatedBlocks.RemoveAt(blockIndex);
            var updated = sections.ToList();
            updated[sectionIndex] = sections[sectionIndex] with { Blocks = updatedBlocks };
            return updated;
        }
        throw new ArgumentException($"No block with id '{cellId}' found.", nameof(cellId));
    }

    private static IReadOnlyList<NotebookSection> MoveBlock(
        IReadOnlyList<NotebookSection> sections, string cellId, int offset)
    {
        for (var sectionIndex = 0; sectionIndex < sections.Count; sectionIndex++) {
            var blocks = sections[sectionIndex].Blocks;
            var blockIndex = FindBlockIndex(blocks, cellId);
            if (blockIndex < 0) {
                continue;
            }
            var targetIndex = blockIndex + offset;
            if (targetIndex < 0 || targetIndex >= blocks.Count) {
                return sections;
            }
            var updatedBlocks = blocks.ToList();
            (updatedBlocks[blockIndex], updatedBlocks[targetIndex]) =
                (updatedBlocks[targetIndex], updatedBlocks[blockIndex]);
            var updated = sections.ToList();
            updated[sectionIndex] = sections[sectionIndex] with { Blocks = updatedBlocks };
            return updated;
        }
        throw new ArgumentException($"No block with id '{cellId}' found.", nameof(cellId));
    }

    private static IReadOnlyList<NotebookSection> UpdateBlock(
        IReadOnlyList<NotebookSection> sections,
        string blockId,
        Func<NotebookBlock, NotebookBlock> transform)
    {
        for (var sectionIndex = 0; sectionIndex < sections.Count; sectionIndex++) {
            var blockIndex = FindBlockIndex(sections[sectionIndex].Blocks, blockId);
            if (blockIndex < 0) {
                continue;
            }
            var updatedBlocks = sections[sectionIndex].Blocks.ToList();
            updatedBlocks[blockIndex] = transform(updatedBlocks[blockIndex]);
            var updated = sections.ToList();
            updated[sectionIndex] = sections[sectionIndex] with { Blocks = updatedBlocks };
            return updated;
        }
        return sections;
    }

    private static int FindBlockIndex(IReadOnlyList<NotebookBlock> blocks, string blockId)
    {
        for (var index = 0; index < blocks.Count; index++) {
            if (GetBlockId(blocks[index]) == blockId) {
                return index;
            }
        }
        return -1;
    }

    private static string? GetBlockId(NotebookBlock block)
        => block switch {
            CodeCellBlock cell => cell.Id,
            ParagraphBlock paragraph => paragraph.Id,
            _ => null,
        };

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

    private void Publish(bool structural = false)
    {
        VerifyAccess();
        Snapshot = CreateSnapshot();
        Changed?.Invoke(Snapshot, structural);
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
