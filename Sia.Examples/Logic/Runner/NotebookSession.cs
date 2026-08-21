using System.Collections.Immutable;

namespace Sia_Examples.Notebook;

public sealed class NotebookSession : IDisposable
{
    private readonly IUiThread _mainThread;
    private readonly MetadataReferenceProvider _references;
    private readonly PackageRegistry _packages = new();

    private NotebookDocument _document;
    private List<CellScript> _scripts = [];
    private Dictionary<string, CodeCellBlock> _owners = [];
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

    public IReadOnlyList<CellScript> Scripts => _scripts;

    public NotebookSessionSnapshot Snapshot { get; private set; }

    public CellState GetState(string scriptId) => _states[scriptId];

    public bool IsScriptEditable(string scriptId) => _owners[scriptId].Editable;

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
        string scriptId,
        string source,
        CancellationToken cancellationToken = default)
    {
        VerifyAccess();
        if (!_scopeKeys.TryGetValue(scriptId, out var scopeKey)
            || _states[scriptId].Source == source) {
            return;
        }

        var highlights = CSharpHighlighter.Classify(source);
        cancellationToken.ThrowIfCancellationRequested();

        SetState(scriptId, state => state with {
            Source = source,
            Highlights = highlights.ToImmutableArray(),
        });
        InvalidateFrom(_scopes[scopeKey], _scopeIndices[scriptId]);
        Publish();
    }

    public string InsertCell(string? afterBlockId, string? scope = null)
    {
        VerifyAccess();
        var cellId = Guid.NewGuid().ToString("N");
        var newCell = new CodeCellBlock(
            cellId, [new CellScript(Guid.NewGuid().ToString("N"), "", "New script")], Editable: true, scope);
        _document = _document with { Sections = InsertBlockAfter(_document.Sections, afterBlockId, newCell) };
        Rebuild();
        Publish(structural: true);
        return cellId;
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

    public string? AddScript(string cellId)
    {
        VerifyAccess();
        string? newScriptId = null;
        _document = _document with {
            Sections = UpdateBlock(_document.Sections, cellId, block => {
                if (block is not CodeCellBlock cell) {
                    return block;
                }
                var script = new CellScript(
                    Guid.NewGuid().ToString("N"), "", NextDefaultScriptName(cell.Scripts));
                newScriptId = script.Id;
                return cell with { Scripts = [.. cell.Scripts, script] };
            }),
        };
        if (newScriptId is null) {
            return null;
        }
        Rebuild();
        Publish(structural: true);
        return newScriptId;
    }

    public void RemoveScript(string scriptId)
    {
        VerifyAccess();
        var owner = _document.Sections
            .SelectMany(static section => section.Blocks)
            .OfType<CodeCellBlock>()
            .FirstOrDefault(cell => cell.Scripts.Any(script => script.Id == scriptId));
        if (owner is null) {
            return;
        }
        if (owner.Scripts.Count <= 1) {
            RemoveCell(owner.Id);
            return;
        }

        _document = _document with {
            Sections = UpdateBlock(_document.Sections, owner.Id, block =>
                block is CodeCellBlock cell
                    ? cell with { Scripts = [.. cell.Scripts.Where(script => script.Id != scriptId)] }
                    : block),
        };
        Rebuild();
        Publish(structural: true);
    }

    public void RenameScript(string scriptId, string? name)
    {
        VerifyAccess();
        var normalized = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        var owner = _document.Sections
            .SelectMany(static section => section.Blocks)
            .OfType<CodeCellBlock>()
            .FirstOrDefault(cell => cell.Scripts.Any(script => script.Id == scriptId));
        if (owner is null) {
            return;
        }
        var current = owner.Scripts.First(script => script.Id == scriptId).Name;
        if (current == normalized) {
            return;
        }

        _document = _document with {
            Sections = UpdateBlock(_document.Sections, owner.Id, block =>
                block is CodeCellBlock cell
                    ? cell with {
                        Scripts = [.. cell.Scripts.Select(script =>
                            script.Id == scriptId ? script with { Name = normalized } : script)],
                    }
                    : block),
        };
        Rebuild();
        Publish(structural: true);
    }

    public void SetCellScope(string cellId, string? scope)
    {
        VerifyAccess();
        var normalized = string.IsNullOrWhiteSpace(scope) ? null : scope.Trim();
        var current = _document.Sections
            .SelectMany(static section => section.Blocks)
            .OfType<CodeCellBlock>()
            .FirstOrDefault(cell => cell.Id == cellId)?.Scope;
        if (current == normalized) {
            return;
        }

        _document = _document with {
            Sections = UpdateBlock(_document.Sections, cellId, block =>
                block is CodeCellBlock cell ? cell with { Scope = normalized } : block),
        };
        Rebuild();
        Publish(structural: true);
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
            : new CodeCellBlock(
                Guid.NewGuid().ToString("N"),
                [new CellScript(Guid.NewGuid().ToString("N"), "", "New script")],
                Editable: true,
                null);
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
                CodeCellBlock cell => cell with {
                    Scripts = [.. cell.Scripts.Select(script =>
                        script with { InitialSource = _states[script.Id].Source })],
                },
                var other => other,
            }).ToList(),
        }).ToList();
        return _document with { Sections = sections };
    }

    public async Task CompileThroughAsync(
        string scriptId,
        CancellationToken cancellationToken = default)
    {
        VerifyAccess();
        if (!TryGetTarget(scriptId, out var scope, out var targetIndex) || IsBusy) {
            return;
        }
        if (scope.HasCompilation(targetIndex)) {
            SetPhases(scope.Scripts, targetIndex, CellPhase.Compiled);
            Publish();
            return;
        }

        using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _operationCancellation = operation;
        try {
            SetPhases(scope.Scripts, targetIndex, CellPhase.Compiling);
            Publish();
            await CompileCoreAsync(scope, targetIndex, operation.Token);
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested) {
            SetPhases(scope.Scripts, targetIndex, CellPhase.Interrupted);
        } finally {
            VerifyAccess();
            _operationCancellation = null;
            Publish();
        }
    }

    public async Task RunThroughAsync(
        string scriptId,
        CancellationToken cancellationToken = default)
    {
        VerifyAccess();
        if (!TryGetTarget(scriptId, out var scope, out var targetIndex) || IsBusy) {
            return;
        }

        using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _operationCancellation = operation;
        try {
            if (!scope.HasCompilation(targetIndex)) {
                SetPhases(scope.Scripts, targetIndex, CellPhase.Compiling);
                Publish();
                if (!await CompileCoreAsync(scope, targetIndex, operation.Token)) {
                    return;
                }
            }

            var compilation = scope.GetCompilation(targetIndex);
            var run = new ActiveRun(scope.Scripts, targetIndex);
            _activeRun = run;
            SetPhases(scope.Scripts, targetIndex, CellPhase.Running);
            ResetFollowingCells(scope.Scripts, targetIndex);
            Publish();

            var result = await NotebookCompiler.ExecuteAsync(compilation.Assembly);
            VerifyAccess();
            if (ReferenceEquals(_activeRun, run)) {
                ApplyRunResult(result, compilation.Program, scope.Scripts, targetIndex);
            }
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested) {
            SetPhases(scope.Scripts, targetIndex, CellPhase.Interrupted);
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
        SetPhases(run.Scripts, run.TargetIndex, CellPhase.Interrupted);
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
        var program = BuildProgram(scope.Scripts, targetIndex);
        scope.SetProgram(program, _references);
        var result = await scope.Compiler!.CompileAsync(cancellationToken);
        VerifyAccess();

        var diagnostics = GroupDiagnosticsByCell(
            result.Diagnostics,
            program,
            scope.Scripts[targetIndex].Id);
        if (!result.Success) {
            scope.RemoveCompilation(targetIndex);
            for (var index = 0; index <= targetIndex; index++) {
                var id = scope.Scripts[index].Id;
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
            var id = scope.Scripts[index].Id;
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
        IReadOnlyList<CellScript> scopeScripts,
        int targetIndex)
    {
        var standardOutput = NotebookProgramBuilder.SliceOutput(result.StdOut, program);
        var standardError = NotebookProgramBuilder.SliceOutput(result.StdErr, program);
        var lastStartedIndex = -1;
        for (var index = 0; index <= targetIndex; index++) {
            if (standardOutput.ContainsKey(scopeScripts[index].Id)) {
                lastStartedIndex = index;
            }
        }

        for (var index = 0; index <= targetIndex; index++) {
            var id = scopeScripts[index].Id;
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
        string fallbackScriptId)
    {
        var result = new Dictionary<string, List<NotebookDiagnostic>>();
        foreach (var diagnostic in diagnostics) {
            var scriptId = diagnostic.InUserCode
                ? program.ResolveCellId(diagnostic.Line - 1)
                : null;
            scriptId ??= fallbackScriptId;
            if (!result.TryGetValue(scriptId, out var scriptDiagnostics)) {
                scriptDiagnostics = [];
                result.Add(scriptId, scriptDiagnostics);
            }
            scriptDiagnostics.Add(diagnostic);
        }
        return result;
    }

    private void InvalidateFrom(ScopeState scope, int startIndex)
    {
        for (var index = startIndex; index < scope.Scripts.Count; index++) {
            var id = scope.Scripts[index].Id;
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
        var scripts = new List<CellScript>();
        var owners = new Dictionary<string, CodeCellBlock>();
        foreach (var block in _document.Sections.SelectMany(static section => section.Blocks)) {
            if (block is not CodeCellBlock cell) {
                continue;
            }
            foreach (var script in cell.Scripts) {
                scripts.Add(script);
                owners[script.Id] = cell;
            }
        }

        var scopeKeys = new Dictionary<string, string>();
        var scopeIndices = new Dictionary<string, int>();
        var groupedScripts = new Dictionary<string, List<CellScript>>();
        foreach (var script in scripts) {
            var owner = owners[script.Id];
            var scopeKey = owner.Scope ?? $"$cell:{owner.Id}";
            scopeKeys.Add(script.Id, scopeKey);
            if (!groupedScripts.TryGetValue(scopeKey, out var scopeScripts)) {
                scopeScripts = [];
                groupedScripts.Add(scopeKey, scopeScripts);
            }
            scopeIndices.Add(script.Id, scopeScripts.Count);
            scopeScripts.Add(script);
        }

        var changedScopeKeys = new HashSet<string>();
        var scopes = new Dictionary<string, ScopeState>();
        foreach (var (scopeKey, scopeScripts) in groupedScripts) {
            if (_scopes.TryGetValue(scopeKey, out var previousScope) && SameScriptIds(previousScope.Scripts, scopeScripts)) {
                scopes[scopeKey] = previousScope;
            } else {
                changedScopeKeys.Add(scopeKey);
                scopes[scopeKey] = new ScopeState(scopeScripts);
            }
        }

        var states = new Dictionary<string, CellState>();
        foreach (var script in scripts) {
            if (!_states.TryGetValue(script.Id, out var existing)) {
                states[script.Id] = CellState.Create(script.InitialSource) with {
                    Highlights = CSharpHighlighter.Classify(script.InitialSource).ToImmutableArray(),
                };
            } else if (changedScopeKeys.Contains(scopeKeys[script.Id])) {
                states[script.Id] = existing with {
                    Phase = CellPhase.Idle,
                    Diagnostics = [],
                    StandardOutput = "",
                    StandardError = "",
                    RenderRequested = false,
                    RenderOutput = "",
                };
            } else {
                states[script.Id] = existing;
            }
        }

        _scripts = scripts;
        _owners = owners;
        _scopeKeys = scopeKeys;
        _scopeIndices = scopeIndices;
        _scopes = scopes;
        _states = states;
        _cellsDirty = true;
    }

    private static bool SameScriptIds(IReadOnlyList<CellScript> a, IReadOnlyList<CellScript> b)
    {
        if (a.Count != b.Count) {
            return false;
        }
        for (var index = 0; index < a.Count; index++) {
            if (a[index].Id != b[index].Id) {
                return false;
            }
        }
        return true;
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

    private static string NextDefaultScriptName(IReadOnlyList<CellScript> existing)
    {
        var names = existing.Select(static script => script.Name).ToHashSet(StringComparer.Ordinal);
        if (!names.Contains("New script")) {
            return "New script";
        }
        var index = 2;
        while (names.Contains($"New script {index}")) {
            index++;
        }
        return $"New script {index}";
    }

    private bool TryGetTarget(
        string scriptId,
        out ScopeState scope,
        out int targetIndex)
    {
        if (_scopeKeys.TryGetValue(scriptId, out var scopeKey)) {
            scope = _scopes[scopeKey];
            targetIndex = _scopeIndices[scriptId];
            return true;
        }

        scope = null!;
        targetIndex = -1;
        return false;
    }

    private NotebookProgram BuildProgram(
        IReadOnlyList<CellScript> scopeScripts,
        int targetIndex)
        => NotebookProgramBuilder.Build(scopeScripts
            .Take(targetIndex + 1)
            .Select(script => (script.Id, _states[script.Id].Source))
            .ToArray());

    private void SetPhases(
        IReadOnlyList<CellScript> scripts,
        int targetIndex,
        CellPhase phase)
    {
        for (var index = 0; index <= targetIndex; index++) {
            SetState(scripts[index].Id, state => state with { Phase = phase });
        }
    }

    private void ResetFollowingCells(IReadOnlyList<CellScript> scripts, int targetIndex)
    {
        for (var index = targetIndex + 1; index < scripts.Count; index++) {
            SetState(scripts[index].Id, state => state with { Phase = CellPhase.Idle });
        }
    }

    private void SetState(string scriptId, Func<CellState, CellState> update)
    {
        var current = _states[scriptId];
        var next = update(current);
        if (next != current) {
            _states[scriptId] = next;
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
            _cellSnapshot = [.. _scripts.Select(script =>
                new NotebookCellSnapshot(script.Id, _states[script.Id]))];
            _cellsDirty = false;
        }
        return new(++_version, _cellSnapshot, _packages.Snapshot);
    }

    private void VerifyAccess()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _mainThread.VerifyAccess();
    }

    private sealed class ScopeState(IReadOnlyList<CellScript> scripts)
    {
        private readonly Dictionary<int, CompilationArtifact> _compilations = [];

        public IReadOnlyList<CellScript> Scripts { get; } = scripts;

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

    private sealed record ActiveRun(IReadOnlyList<CellScript> Scripts, int TargetIndex);
}
