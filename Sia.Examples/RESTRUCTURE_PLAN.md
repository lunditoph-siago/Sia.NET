# Sia.Examples restructure plan

Scope: `Sia.Examples/` only (excludes `Sia.Examples.V1/`, `Sia.Examples.V2/`,
`Sia.Examples.Tests/`). Goal: bring the folder layout in line with the
repo-wide convention in `CLAUDE.md` — "Arrange by concern, not by type" —
which this codebase already follows in `Logic/Compilation`, `Logic/Provider`,
`Logic/Runner`, `Logic/Storage`, `Console/Layout`, and `Elements/Cells`, but
has drifted from in a few places where new features were added as flat file
dumps instead.

All moves are **namespace-preserving** (files keep their current
`namespace`; only their folder changes) — C# doesn't require folder to match
namespace, so this is a low-risk, purely organizational change. The one
exception (`IndexedDbNotebookStorage.cs`) doesn't change namespace either,
just folder.

## Precedent used to validate this plan

`Sia/Entities/` and `Sia/Worlds/` (the two largest concern folders in
`Sia.NET/Sia`, the repo's own reference implementation) were used to check
the shape of this plan against real precedent:

- Top level splits **by concern** (`Entities/`, `Worlds/`, `Events/`,
  `Systems/`...), not by C# role.
- *Within* a concern folder, further nesting by role (`Interfaces/`,
  `Extensions/`, `Delegates/`, `Exceptions/`) or by cohesive sub-concept
  (`Entities/Hosts/`, `Worlds/Actor/`, `Worlds/Commands/`) is normal and
  expected once that folder gets large — `Worlds/Actor/` and
  `Worlds/Commands/` prove a subfolder doesn't need many files to be
  justified (3 files and 1 file respectively), it just needs to be a
  distinct, nameable concept.
- A role bucket like `Interfaces/` is only worth creating once there's more
  than one file of that role in the same concern — a single interface
  (e.g. `IEditorView.cs`) stays wherever it naturally belongs instead.
- 3-level nesting exists precedent (`Entities/Hosts/BufferEntityHosts/`), so
  `Editor/State/Text/` etc. is not unusually deep.

## Full target tree

```
Sia.Examples/
├── Dom/                    unchanged — 4 files, too small to split
├── Browser/                BrowserDomBackend.cs, BrowserMainThread.cs,
│                           BrowserResourceLoader.cs
│                           (IndexedDbNotebookStorage.cs moves out, see below)
├── Console/                unchanged — 18 root files (5 of which are
│                           ConsoleDomBackend.*.cs partials, i.e. one type),
│                           comparable in scale to Worlds/'s 11 root files;
│                           Layout/ already split out
│
├── Logic/
│   ├── Compilation/, Highlighter/, Provider/, Runner/, Storage/   unchanged
│   ├── Document/           NEW — the notebook document/block model:
│   │                       CellScript.cs, CodeCellBlock.cs, CodeInline.cs,
│   │                       Inline.cs, ListBlock.cs, NotebookBlock.cs,
│   │                       NotebookBlockKind.cs, NotebookDocument.cs,
│   │                       NotebookDocumentParser.cs,
│   │                       NotebookDocumentSerializer.cs, NotebookSection.cs,
│   │                       ParagraphBlock.cs, TextInline.cs
│   └── IUiThread.cs        stays — namespace is bare `Sia_Examples`, not
│                           `.Notebook` like the rest of Logic/; a
│                           pre-existing misplacement too small to warrant
│                           its own folder, noted here rather than moved
│
├── Elements/
│   ├── Cells/              unchanged — already a self-contained concern
│   ├── Editor/             see detailed breakdown below (69 files)
│   ├── Notebook/           NEW — the "render one open notebook" family:
│   │                       INotebookView.cs, BrowserNotebookView.cs,
│   │                       NotebookViewComponent.cs, NotebookViewProps.cs,
│   │                       NotebookCellsComponent.cs, NotebookCellsProps.cs,
│   │                       BrowserCellView.cs, NotebookPackagesComponent.cs,
│   │                       NotebookPackagesProps.cs, BrowserPackagePanel.cs,
│   │                       PackageView.cs, PackageCountView.cs,
│   │                       BrowserNotebookFileTree.cs, NotebookElementIds.cs,
│   │                       NotebookWorkspace.cs
│   └── (root)              stays — the app-shell family (picking/switching
│                           which notebook is open, not its content):
│                           ExampleApp.cs, ExampleAppProps.cs,
│                           ExampleAppState.cs, ExampleItemView.cs,
│                           ExampleViewer.cs, IRenderHost.cs, RenderEffect.cs,
│                           BrowserApplication.cs, BrowserApplicationView.cs,
│                           DomApplication.cs
```

### `Browser/IndexedDbNotebookStorage.cs` → `Logic/Storage/`

Same family as `Logic/Storage/IndexedDbWorkspaceStorage.cs` — both implement
`IWorkspaceStorage`, both are `#if BROWSER` + JSImport. This one was missed
during the earlier Storage move and should join its three siblings
(`FileSystemWorkspaceStorage`, `InMemoryWorkspaceStorage`,
`IndexedDbWorkspaceStorage`) instead of sitting alone in `Browser/`.

### `Elements/Editor/` detailed breakdown (69 files)

CodeMirror 6 is the evident model for this subsystem (`EditorState.Apply`,
`ChangeSet`/`ChangeDesc`, `EditorSelection`, `Text` rope,
`RangeSet<Decoration>` all mirror it closely), so its own package boundaries
(`@codemirror/state`, `@codemirror/view`, `@codemirror/commands`,
`@codemirror/autocomplete`) are used as the top-level split instead of an
invented one. First pass grouped by C# role (state vs. view) and was wrong —
it split single features like "Lines" and "Selection" across two folders;
this version keeps each feature's state and view halves together.

```
Editor/
├── State/                  document model layer (@codemirror/state)
│   ├── Text/               Text.cs, TextLeaf.cs, TextNode.cs, TextOpen.cs,
│   │                       TextCursor.cs, TextMath.cs, TextConstants.cs,
│   │                       Line.cs, Range.cs, RangeSet.cs, CharCategory.cs,
│   │                       CharUtil.cs, ColumnUtil.cs, MapMode.cs
│   ├── Changes/            ChangeDesc.cs, ChangeSet.cs, ChangeSpec.cs,
│   │                       ChangeHelpers.cs, ChangeSectionBuilder.cs,
│   │                       ChangeSectionIterator.cs, TextDiff.cs,
│   │                       TextDifference.cs
│   ├── Selection/          EditorSelection.cs, SelectionRange.cs, SelFlag.cs
│   ├── EditorState.cs      root aggregate: Doc + Selection + Decorations +
│   │                       LineIdentities
│   ├── EditorUpdate.cs
│   ├── EditorLineIdentities.cs
│   └── EditorLineUpdate.cs
│
├── View/                   rendering/reconciliation layer (@codemirror/view)
│   ├── Layout/              moved in as-is — height-map/viewport is already
│   │                       a view concern, shouldn't sit as a View/ sibling
│   ├── Decorations/         Decoration.cs, DecorationKind.cs,
│   │                       DecorationSet.cs, EditorDecorations.cs,
│   │                       LineDecorator.cs, StyledRun.cs
│   ├── Lines/                EditorLineItem.cs, EditorLinesCache.cs,
│   │                       EditorLinesComponent.cs, EditorLinesProps.cs,
│   │                       EditorLineView.cs, EditorActiveLineView.cs,
│   │                       LineReuseMap.cs
│   ├── IEditorView.cs, BrowserEditorView.cs
│   ├── EditorViewComponent.cs, EditorViewProps.cs
│   └── EditorDocumentView.cs, EditorStatusView.cs, EditorSelectionView.cs
│
├── Commands/                editing command layer (@codemirror/commands)
│                            CommandTarget.cs, StateCommand.cs,
│                            GroupMovement.cs, CursorCommands.cs,
│                            DeleteCommands.cs, LineCommands.cs,
│                            SelectionCommands.cs, TextCommands.cs
│
├── Completion/               unchanged: CompletionCandidate.cs,
│                            CompletionIdentifier.cs, CompletionResult.cs,
│                            CompletionTrigger.cs, CSharpCompletionProvider.cs
│
├── Workspace/                unchanged — file-tree/workspace management,
│                            unrelated to the CodeMirror model
│
└── (root)                    page-level orchestration/entry points:
                             BrowserEditorHost.cs,
                             BrowserEditorPage(.Dialogs/.Explorer/.Layout).cs,
                             BrowserEditorRegistry.cs,
                             EditorProjectCompiler.cs
```

## Execution order

~90 files total move. Do it in 4 steps, each with its own build + test pass
via `.dotnet/dotnet build`/`test` (toolchain lives at the monorepo root:
`C:\Users\Seele\Documents\GitHub\Sia\.dotnet`, not inside `Sia.NET/`),
smallest/lowest-risk first:

1. `Browser/IndexedDbNotebookStorage.cs` → `Logic/Storage/` (1 file)
2. `Logic/` root → `Logic/Document/` (13 files, all `Sia_Examples.Notebook`
   namespace already — no using changes needed)
3. `Elements/` root → `Elements/Notebook/` (14 files, all
   `Sia_Examples.Notebook`/`Sia_Examples` already — no using changes needed)
4. `Elements/Editor/` internal reshuffle (69 files, largest, done last)

## Status

Done. All 4 steps executed via `git mv` (namespace-preserving), with a build
+ test pass after each step and a browser smoke test (notebook + Editor Lab)
against the final published output.
