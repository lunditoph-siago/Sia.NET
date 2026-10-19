import { emit, emitBarrier, resolveBarrier, takeEventBatch } from './notebook-runtime.js';
import { pinEditorLines } from './editor-lines.js';
import { attachCommandSurface, invokeWorkbenchCommand, resolveShortcut } from './editor-keybindings.js';
import { scheduleCompletionWindow } from './editor-overlay.js';

const editorHandlers = new Map();
const selectionSyncPending = new WeakSet();
const editorSelectionUpdates = new WeakMap();
const editorSurfaceHandlers = new WeakMap();
const findSelectionRanges = new WeakMap();
const caretMarkerText = '\u200b';
const caretMarkerSelector = '[data-editor-caret-marker]';

function lineOf(surface, node) {
    for (let current = node; current && current !== surface; current = current.parentNode) {
        if (current.nodeType === Node.ELEMENT_NODE && current.dataset?.ln !== undefined) {
            return current;
        }
    }
    return null;
}

function isRealLine(node) {
    return node?.nodeType === Node.ELEMENT_NODE && node.dataset?.ln !== undefined;
}

function mountedLineRange(surface) {
    const lines = surface.querySelectorAll(':scope .editor-line[data-ln]');
    if (!lines.length) {
        return null;
    }
    return {
        start: Number(lines[0].dataset.ln),
        end: Number(lines[lines.length - 1].dataset.ln) + 1,
    };
}

function markerContent(marker) {
    const text = marker.textContent ?? '';
    if (marker.dataset.editorCaretMarker === 'start' && text.startsWith(caretMarkerText)) {
        return text.slice(1);
    }
    if (marker.dataset.editorCaretMarker === 'end' && text.endsWith(caretMarkerText)) {
        return text.slice(0, -1);
    }
    return text;
}

export function getEditorText(node) {
    if (node.dataset?.editorCaretMarker !== undefined) {
        return markerContent(node);
    }
    if (!node.childNodes?.length) {
        return node.textContent ?? '';
    }
    return [...node.childNodes].map(getEditorText).join('');
}

export function getEditorTextPoint(line, column) {
    let remaining = column;
    const walker = document.createTreeWalker(line, NodeFilter.SHOW_TEXT);
    let node;
    let last = null;
    while ((node = walker.nextNode())) {
        const marker = node.parentElement?.closest?.(caretMarkerSelector);
        const start = marker?.dataset.editorCaretMarker === 'start'
            && marker.firstChild === node && node.nodeValue.startsWith(caretMarkerText) ? 1 : 0;
        const end = marker?.dataset.editorCaretMarker === 'end'
            && marker.lastChild === node && node.nodeValue.endsWith(caretMarkerText) ? 1 : 0;
        const length = node.nodeValue.length - start - end;
        last = { node, offset: node.nodeValue.length - end };
        if (remaining <= length) {
            return { node, offset: start + remaining };
        }
        remaining -= length;
    }
    return last ?? { node: line, offset: 0 };
}

function readNativeText(node) {
    if (node.classList?.contains('editor-spacer')) {
        return '';
    }
    if (node.dataset?.editorCaretMarker !== undefined) {
        return markerContent(node);
    }
    if (!node.childNodes?.length) {
        return node.tagName === 'BR' ? '\n' : (node.textContent ?? '');
    }
    if (node.childNodes.length === 1 && node.childNodes[0].tagName === 'BR') {
        return '';
    }
    let text = '';
    let previousBlock = false;
    let hasContent = false;
    for (const child of node.childNodes) {
        if (child.classList?.contains('editor-spacer')) {
            continue;
        }
        const block = isRealLine(child) || child.tagName === 'DIV' || child.tagName === 'P';
        if (hasContent && (block || previousBlock)) {
            text += '\n';
        }
        text += readNativeText(child);
        previousBlock = block;
        hasContent = true;
    }
    return text;
}

function columnInLine(line, node, offset) {
    const range = document.createRange();
    range.setStart(line, 0);
    range.setEnd(node, offset);
    return getEditorText(range.cloneContents()).length;
}

function clearCaretMarkers(surface, preserveSelection) {
    const markers = [...surface.querySelectorAll(caretMarkerSelector)];
    if (!preserveSelection) {
        for (const marker of markers) {
            const text = markerContent(marker);
            if (text) {
                marker.replaceWith(document.createTextNode(text));
            } else {
                marker.remove();
            }
        }
        return;
    }

    const selection = window.getSelection();
    const pointInMarker = (node, offset) => {
        const element = node?.nodeType === Node.ELEMENT_NODE ? node : node?.parentElement;
        const marker = element?.closest?.(caretMarkerSelector);
        if (!marker || !surface.contains(marker)) {
            return null;
        }
        const range = document.createRange();
        range.setStart(marker, 0);
        range.setEnd(node, offset);
        const length = range.toString().length;
        const text = marker.textContent;
        const includesMarker = marker.dataset.editorCaretMarker === 'start'
            ? text.startsWith(caretMarkerText) && length > 0
            : text.endsWith(caretMarkerText) && length === text.length;
        return {
            marker,
            offset: length - Number(includesMarker),
        };
    };
    const anchor = pointInMarker(selection?.anchorNode, selection?.anchorOffset ?? 0);
    const head = pointInMarker(selection?.focusNode, selection?.focusOffset ?? 0);
    const replacements = new Map();
    for (const marker of markers) {
        const text = markerContent(marker);
        if (text) {
            const node = document.createTextNode(text);
            replacements.set(marker, { node });
            marker.replaceWith(node);
        } else {
            replacements.set(marker, {
                line: marker.closest('[data-ln]'),
                position: marker.dataset.editorCaretMarker,
            });
            marker.remove();
        }
    }
    if (anchor || head) {
        const boundaryPoint = (replacement) => {
            const walker = document.createTreeWalker(replacement.line, NodeFilter.SHOW_TEXT);
            let first = null;
            let last = null;
            for (let node = walker.nextNode(); node; node = walker.nextNode()) {
                first ??= node;
                last = node;
            }
            if (replacement.position === 'start' && first) {
                return { node: first, offset: 0 };
            }
            if (replacement.position === 'end' && last) {
                return { node: last, offset: last.nodeValue.length };
            }
            return {
                node: replacement.line,
                offset: replacement.position === 'start' ? 0 : replacement.line.childNodes.length,
            };
        };
        const mappedPoint = (point, node, offset) => {
            if (!point) {
                return { node, offset };
            }
            const replacement = replacements.get(point.marker);
            if (replacement.node) {
                return {
                    node: replacement.node,
                    offset: Math.min(point.offset, replacement.node.nodeValue.length),
                };
            }
            return boundaryPoint(replacement);
        };
        const mappedAnchor = mappedPoint(anchor, selection.anchorNode, selection.anchorOffset);
        const mappedHead = mappedPoint(head, selection.focusNode, selection.focusOffset);
        selection.setBaseAndExtent(
            mappedAnchor.node,
            mappedAnchor.offset,
            mappedHead.node,
            mappedHead.offset,
        );
    }
}

const observeOptions = {
    childList: true,
    characterData: true,
    subtree: true,
    characterDataOldValue: true,
};
const surfaceObservers = new Map();
const pendingEditorNodes = new WeakMap();

function pendingEditorSurface(node) {
    for (let current = node; current; current = current.parentNode) {
        const surface = pendingEditorNodes.get(current);
        if (surface) return surface;
    }
    return null;
}

function clearPendingEditorNodes(surface, state) {
    for (const node of state.pendingNodes) {
        if (pendingEditorNodes.get(node) === surface) pendingEditorNodes.delete(node);
    }
    state.pendingNodes.clear();
}

function resolveSurface(node) {
    const element = node?.nodeType === Node.ELEMENT_NODE ? node : node?.parentElement;
    return pendingEditorSurface(node)
        ?? element?.closest?.('.editor-lines')
        ?? element?.closest?.('.editor-container')
            ?.querySelector(':scope > .editor-scroll > .editor-lines')
        ?? null;
}

export function ignoreOwnWrites(write, scopeNode) {
    const surface = resolveSurface(scopeNode);
    const state = surface && surfaceObservers.get(surface);
    if (!state) {
        return write();
    }
    if (state.depth === 0) {
        state.observer.disconnect();
    }
    state.depth++;
    try {
        return write();
    } finally {
        state.depth--;
        if (state.depth === 0 && !state.reconnectScheduled) {
            state.reconnectScheduled = true;
            queueMicrotask(() => {
                state.reconnectScheduled = false;
                if (state.depth === 0 && surfaceObservers.get(surface) === state) {
                    state.observer.observe(surface, observeOptions);
                }
            });
        }
    }
}

export function writeEditorDom(write, scopeNode, insertedNode) {
    const surface = resolveSurface(scopeNode);
    const state = surface && surfaceObservers.get(surface);
    const element = scopeNode?.nodeType === Node.ELEMENT_NODE ? scopeNode : scopeNode?.parentElement;
    const content = element === surface
        || element?.closest?.('.editor-scroll')
        || element?.closest?.('.editor-status')
        || pendingEditorSurface(scopeNode);
    if (state?.pendingWrites && content) {
        if (insertedNode) {
            pendingEditorNodes.set(insertedNode, surface);
            state.pendingNodes.add(insertedNode);
        }
        state.pendingWrites.push(write);
        return;
    }
    return ignoreOwnWrites(write, scopeNode);
}

export function setEditorUpdating(surface, updating) {
    const state = surfaceObservers.get(surface);
    if (updating) {
        if (state) {
            state.pendingWrites ??= [];
        }
        ignoreOwnWrites(() => surface.dataset.editorUpdating = 'true', surface);
        return;
    }

    if (state?.nativeEditPending) {
        return;
    }

    const writes = state?.pendingWrites ?? [];
    if (state) {
        state.pendingWrites = null;
        clearPendingEditorNodes(surface, state);
    }
    ignoreOwnWrites(() => {
        for (const write of writes) {
            write();
        }
        surface.dataset.editorUpdating = 'false';
        if (state) {
            state.nativeSequence = 0;
            state.nativeWindow = null;
        }
    }, surface);
    if (state?.pendingSelection) {
        const selection = state.pendingSelection;
        state.pendingSelection = null;
        setEditorSelection(surface, ...selection);
    }
}

export function prepareEditorReconciliation(surface, sequence) {
    const state = surfaceObservers.get(surface);
    if (!state) {
        return;
    }
    if (state.composing || sequence !== state.nativeEditGeneration) {
        return;
    }
    state.nativeEditPending = false;
    state.pendingWrites = [];
    clearPendingEditorNodes(surface, state);
    state.pendingSelection = null;
}

export function takeEditorInput(surface, sourceBoundary = true) {
    return editorSurfaceHandlers.get(surface)?.takeInput(sourceBoundary) ?? '';
}

export function attachEditorSurface(cellId, surface, restoreFocus) {
    if (editorHandlers.has(cellId)) {
        throw new Error(`Editor '${cellId}' is already attached.`);
    }
    surface.dataset.editorBinding = cellId;

    let nativeEditSnapshot = null;
    let knownSelection;
    let modelSelectionOnly = false;
    let pointerFocus = false;
    let composing = false;
    let compositionEnding = false;
    let nativeCommitGeneration = 0;
    let nativeCommitTimer;
    let commandSequence = 0;
    let acknowledgedCommandSequence = 0;
    let pointerSelection = false;
    const pendingInputs = new Map();
    const clipboardOperations = new Map();
    let disposed = false;

    const hasPendingCommand = () => acknowledgedCommandSequence < commandSequence;
    const emitInput = (eventType, arguments_, barrier) => {
        const input = `${++commandSequence}:${eventType}:${arguments_}`;
        pendingInputs.set(commandSequence, input);
        const payload = `input:${cellId}:${input}`;
        if (barrier) emitBarrier(payload, barrier);
        else emit(payload);
    };
    const emitCommand = (eventType, selection, payload) => {
        if (nativeEditSnapshot && !composing && !compositionEnding) reconcileNativeEdit();
        pointerSelection = false;
        emitInput(eventType, `${commandSequence + 1}:${selectionArguments(selection)}:${payload}`);
    };
    const readSelection = () => {
        const selection = window.getSelection();
        if (!selection?.rangeCount || !surface.contains(selection.anchorNode)) {
            return null;
        }
        const anchorLine = lineOf(surface, selection.anchorNode);
        const headLine = lineOf(surface, selection.focusNode);
        if (
            !anchorLine ||
            !headLine ||
            !surface.contains(anchorLine) ||
            !surface.contains(headLine)
        ) {
            return null;
        }
        return {
            anchorLine,
            anchorLineIndex: Number(anchorLine.dataset.ln),
            anchorColumn: columnInLine(anchorLine, selection.anchorNode, selection.anchorOffset),
            headLine,
            headLineIndex: Number(headLine.dataset.ln),
            headColumn: columnInLine(headLine, selection.focusNode, selection.focusOffset),
        };
    };
    const readSelectionByDomOrder = () => {
        const selection = window.getSelection();
        if (!selection?.rangeCount || !surface.contains(selection.anchorNode)) {
            return null;
        }
        const normalizeText = (value) =>
            value.replaceAll('\r\n', '\n').replaceAll('\r', '\n');
        const start = nativeEditSnapshot?.range?.start
            ?? surfaceObservers.get(surface)?.nativeWindow?.start
            ?? mountedLineRange(surface)?.start ?? 0;
        const topLevelPoint = (node, offset) => {
            if (!surface.contains(node)) {
                return null;
            }
            const range = document.createRange();
            range.setStart(surface, 0);
            range.setEnd(node, offset);
            const lines = normalizeText(readNativeText(range.cloneContents())).split('\n');
            return {
                line: lineOf(surface, node),
                lineIndex: start + lines.length - 1,
                column: lines.at(-1).length,
            };
        };
        const anchor = topLevelPoint(selection.anchorNode, selection.anchorOffset);
        const head = topLevelPoint(selection.focusNode, selection.focusOffset);
        if (!anchor || !head) {
            return null;
        }
        return {
            anchorLine: anchor.line,
            anchorLineIndex: anchor.lineIndex,
            anchorColumn: anchor.column,
            headLine: head.line,
            headLineIndex: head.lineIndex,
            headColumn: head.column,
        };
    };
    const currentSelection = () => surfaceObservers.get(surface)?.nativeWindow
        ? readSelectionByDomOrder() ?? readSelection()
        : readSelection();
    const selectionCoordinates = (selection) =>
        selection
            ? {
                  anchorLineIndex: selection.anchorLineIndex,
                  anchorColumn: selection.anchorColumn,
                  headLineIndex: selection.headLineIndex,
                  headColumn: selection.headColumn,
              }
            : null;
    const selectionArguments = (selection) =>
        selection
            ? `${selection.anchorLineIndex}:${selection.anchorColumn}:` +
              `${selection.headLineIndex}:${selection.headColumn}`
            : '-1:-1:-1:-1';
    const sameSelection = (left, right) =>
        left === right ||
        (left &&
            right &&
            left.anchorLineIndex === right.anchorLineIndex &&
            left.anchorColumn === right.anchorColumn &&
            left.headLineIndex === right.headLineIndex &&
            left.headColumn === right.headColumn);
    const rememberSelection = (selection, rendered = true) => {
        knownSelection = selectionCoordinates(selection);
        modelSelectionOnly = !rendered;
    };

    const emitSelection = () => {
        const selection = currentSelection();
        if (!selection || sameSelection(selection, knownSelection)) {
            return;
        }
        rememberSelection(selection);
        emitInput('sel', `${selectionArguments(selection)}:` +
            `${surface.dataset.editorDocumentRevision ?? 0}:${surfaceObservers.get(surface).nativeSequence}`);
    };

    const usingModelSelection = () => {
        const range = surfaceObservers.get(surface)?.nativeWindow;
        const rendered = line => range
            ? line >= range.start && line < range.end
            : surface.querySelector(`[data-ln="${line}"]`);
        return modelSelectionOnly || knownSelection && (
            !rendered(knownSelection.anchorLineIndex) || !rendered(knownSelection.headLineIndex));
    };

    const reportSelection = () => {
        const state = surfaceObservers.get(surface);
        if (
            document.activeElement !== surface ||
            selectionSyncPending.has(surface) ||
            usingModelSelection() ||
            nativeEditSnapshot ||
            (state?.pendingWrites && !state.nativeEditPending) ||
            composing ||
            compositionEnding
        ) {
            return;
        }
        emitSelection();
    };

    const armNativeEditSnapshot = (inputType, event) => {
        if ((composing || compositionEnding) && nativeEditSnapshot) {
            nativeEditSnapshot.inputType = inputType;
            nativeEditSnapshot.data = event?.data ?? null;
            return;
        }
        if (nativeEditSnapshot) reconcileNativeEdit();
        clearCaretMarkers(surface, true);
        const state = surfaceObservers.get(surface);
        nativeEditSnapshot = {
            before: selectionCoordinates(currentSelection()) ?? knownSelection,
            inputType,
            data: event?.data ?? null,
            replacesSelection: false,
            range: state.nativeWindow ?? mountedLineRange(surface),
            revision: Number(surface.dataset.editorDocumentRevision ?? 0),
            nativeSequence: state.nativeSequence,
            selectionSource: pointerSelection ? 1 : usingModelSelection() ? 2 : 0,
        };
        state.nativeEditPending = true;
        state.nativeEditGeneration++;
        state.pendingWrites ??= [];
        surface.dataset.editorUpdating = 'true';
        if (!composing) scheduleNativeCommit(0);
    };

    const readNativeMutation = () => {
        clearCaretMarkers(surface, true);
        const snapshot = nativeEditSnapshot;
        const after = readSelectionByDomOrder() ?? readSelection();
        nativeEditSnapshot = null;
        rememberSelection(after);
        if (!snapshot) return '';
        const state = surfaceObservers.get(surface);
        const sequence = state.nativeEditGeneration;
        const text = readNativeText(surface);
        state.nativeSequence = sequence;
        state.nativeWindow = snapshot.range && {
            start: snapshot.range.start,
            end: snapshot.range.start + text.split('\n').length,
        };
        return ['c', sequence, snapshot.revision, snapshot.nativeSequence, snapshot.selectionSource,
            Number(snapshot.replacesSelection),
            snapshot.range?.start ?? -1, snapshot.range?.end ?? -1,
            selectionArguments(snapshot.before), selectionArguments(after),
            encodeURIComponent(snapshot.inputType), encodeURIComponent(text)].join(':');
    };

    const reconcileNativeEdit = () => {
        if (!nativeEditSnapshot) return;
        cancelNativeCommit();
        const mutation = readNativeMutation();
        if (mutation) emitInput('mut', mutation);
    };

    const observer = new MutationObserver(() => {
        if (composing || !nativeEditSnapshot) return;
        if (compositionEnding) scheduleNativeCommit(20);
        else scheduleNativeCommit(0);
    });

    const cancelNativeCommit = () => {
        if (nativeCommitTimer !== undefined) {
            clearTimeout(nativeCommitTimer);
            nativeCommitTimer = undefined;
        }
    };

    const scheduleNativeCommit = (delay = 20) => {
        cancelNativeCommit();
        const generation = ++nativeCommitGeneration;
        nativeCommitTimer = setTimeout(() => {
            nativeCommitTimer = undefined;
            if (composing || generation !== nativeCommitGeneration) {
                return;
            }
            compositionEnding = false;
            reconcileNativeEdit();
        }, delay);
    };

    const commandSelection = () =>
        hasPendingCommand() || usingModelSelection() || surfaceObservers.get(surface)?.pendingWrites
            ? null
            : (readSelection() ?? readSelectionByDomOrder());
    const emitEditorCommand = (command, fallback = '', generation = '') =>
        emitCommand('command', commandSelection(), `${command}:${fallback}:${generation}`);
    const emitTextCommand = (text, isolateHistory = false) =>
        emitCommand(isolateHistory ? 'paste' : 'text', commandSelection(), encodeURIComponent(text));
    const emitHistoryCommand = (redo) => {
        if (compositionEnding) {
            compositionEnding = false;
            reconcileNativeEdit();
        }
        emitEditorCommand(redo ? 'Redo' : 'Undo');
    };
    const emitInputCommand = (event, command) => {
        event.preventDefault();
        command();
    };

    const completionRoot = surface.closest('.editor-container');
    const completionItem = (event) => event.target.closest?.('[data-completion-index]');
    const preserveCompletionFocus = (event) => {
        if (event.pointerType === 'mouse' && completionItem(event)) event.preventDefault();
        if (!completionItem(event)) markCompletionScroll(event);
    };
    const markCompletionScroll = event => {
        const list = event.target.closest?.('.completion-list');
        if (list) list.dataset.completionReveal = 'false';
    };
    const acceptCompletion = (event) => {
        const item = completionItem(event);
        if (!item || composing || compositionEnding) return;
        event.preventDefault();
        if (item.getAttribute('aria-disabled') === 'true') return;
        if (nativeEditSnapshot) reconcileNativeEdit();
        emitInput('suggestion', `${item.dataset.completionGeneration}:${item.dataset.completionIndex}`);
        surface.focus({ preventScroll: true });
    };
    completionRoot?.addEventListener('pointerdown', preserveCompletionFocus);
    completionRoot?.addEventListener('click', acceptCompletion);
    const scrollCompletion = event => {
        if (event.target.matches?.('.completion-list')) scheduleCompletionWindow(event.target);
    };
    completionRoot?.addEventListener('scroll', scrollCompletion, true);
    completionRoot?.addEventListener('wheel', markCompletionScroll, { passive: true });
    completionRoot?.addEventListener('touchmove', markCompletionScroll, { passive: true });

    const handlers = {
        input(event) {
            if (nativeEditSnapshot && !composing && !compositionEnding) {
                nativeEditSnapshot.replacesSelection = event.inputType?.startsWith('insert')
                    || event.inputType?.startsWith('delete') || false;
                reconcileNativeEdit();
            }
        },
        copy(event) {
            copySelection(event, false);
        },
        cut(event) {
            copySelection(event, true);
        },
        paste(event) {
            if (
                composing ||
                compositionEnding ||
                !event.clipboardData?.types.includes('text/plain')
            ) {
                return;
            }
            const text = event.clipboardData.getData('text/plain');
            emitInputCommand(event, () =>
                emitTextCommand(text, true),
            );
        },
        focus() {
            if (!pointerFocus && knownSelection) {
                restoreFocus?.();
                setEditorSelection(surface, knownSelection.anchorLineIndex, knownSelection.anchorColumn,
                    knownSelection.headLineIndex, knownSelection.headColumn);
            }
            pointerFocus = false;
            emit(`editor-focus:${cellId}`);
        },
        beforeinput(event) {
            selectionSyncPending.delete(surface);
            if (!event.isComposing && !composing
                && (event.inputType === 'historyUndo' || event.inputType === 'historyRedo')) {
                emitInputCommand(event, () => emitHistoryCommand(event.inputType === 'historyRedo'));
                return;
            }
            if (
                !composing &&
                !compositionEnding &&
                event.inputType === 'insertText' &&
                event.data !== null
            ) {
                emitInputCommand(event, () => emitTextCommand(event.data));
                return;
            }
            if (
                !composing &&
                !compositionEnding &&
                (event.inputType === 'insertParagraph' || event.inputType === 'insertLineBreak')
            ) {
                emitInputCommand(event, () => emitEditorCommand('Newline'));
                return;
            }
            if (!composing && !compositionEnding && event.inputType === 'deleteContentBackward') {
                emitInputCommand(event, () => emitEditorCommand('Backspace'));
                return;
            }
            if (!composing && !compositionEnding && event.inputType === 'deleteWordBackward') {
                emitInputCommand(event, () => emitEditorCommand('DeleteWordLeft'));
                return;
            }
            if (!composing && !compositionEnding && event.inputType === 'deleteContentForward') {
                emitInputCommand(event, () => emitEditorCommand('Delete'));
                return;
            }
            if (!composing && !compositionEnding && event.inputType === 'deleteWordForward') {
                emitInputCommand(event, () => emitEditorCommand('DeleteWordRight'));
                return;
            }
            if (!composing && !compositionEnding && event.inputType === 'deleteByCut') {
                emitInputCommand(event, () => emitTextCommand(''));
                return;
            }
            if (
                !composing &&
                !compositionEnding &&
                (event.inputType === 'insertFromPaste' || event.inputType === 'insertFromDrop')
            ) {
                const text = event.data ?? event.dataTransfer?.getData('text/plain');
                if (text !== undefined && text !== null) {
                    emitInputCommand(event, () =>
                        emitTextCommand(text, true),
                    );
                    return;
                }
            }
            armNativeEditSnapshot(event.inputType || 'insertText', event);
        },
        pointerdown() {
            pointerFocus = document.activeElement !== surface;
            pointerSelection = true;
            selectionSyncPending.delete(surface);
            modelSelectionOnly = false;
            knownSelection = null;
        },
        pointerup() {
            pointerFocus = false;
            reportSelection();
        },
        selectionchange() {
            if (selectionSyncPending.has(surface)) {
                selectionSyncPending.delete(surface);
                return;
            }
            reportSelection();
        },
        keydown(event) {
            selectionSyncPending.delete(surface);
            if (event.isComposing || composing) {
                return;
            }
            const result = resolveShortcut(event, surface);
            if (!result) return;
            const history = result.binding?.editor && (result.binding.command === 'Undo' || result.binding.command === 'Redo');
            if (compositionEnding && result.binding?.editor && !history) return;
            event.preventDefault();
            if (history) {
                emitHistoryCommand(result.binding.command === 'Redo');
            } else if (result.binding?.editor) {
                const generation = surface.closest('.editor-container')?.querySelector('[data-completion-generation]')?.dataset.completionGeneration ?? '';
                emitEditorCommand(result.binding.command, result.fallback?.command, generation);
            } else if (result.binding) {
                invokeWorkbenchCommand(result.binding.command, surface);
            }
        },
        compositionstart() {
            if (nativeEditSnapshot && !composing) reconcileNativeEdit();
            selectionSyncPending.delete(surface);
            cancelNativeCommit();
            composing = true;
            compositionEnding = false;
            nativeCommitGeneration++;
            armNativeEditSnapshot('insertCompositionText');
            const state = surfaceObservers.get(surface);
            state.composing = true;
            const selectionUpdate = editorSelectionUpdates.get(surface);
            if (selectionUpdate) {
                cancelAnimationFrame(selectionUpdate.frame);
                editorSelectionUpdates.delete(surface);
            }
        },
        compositionend(event) {
            if (nativeEditSnapshot) {
                nativeEditSnapshot.replacesSelection = (event?.data?.length ?? 0) > 0;
            }
            composing = false;
            surfaceObservers.get(surface).composing = false;
            compositionEnding = true;
            scheduleNativeCommit();
        },
        dispose() {
            disposed = true;
            detachCommands();
            delete surface.dataset.editorBinding;
            completionRoot?.removeEventListener('pointerdown', preserveCompletionFocus);
            completionRoot?.removeEventListener('click', acceptCompletion);
            completionRoot?.removeEventListener('scroll', scrollCompletion, true);
            completionRoot?.removeEventListener('wheel', markCompletionScroll);
            completionRoot?.removeEventListener('touchmove', markCompletionScroll);
            for (const operation of [...clipboardOperations.values()]) finishClipboard(operation, false);
            clipboardOperations.clear();
            surface.dataset.editorClipboardPending = 'false';
            nativeCommitGeneration++;
            cancelNativeCommit();
            observer.disconnect();
            clearPendingEditorNodes(surface, surfaceObservers.get(surface));
            surfaceObservers.delete(surface);
            pendingInputs.clear();
        },
        acknowledge(sequence) {
            acknowledgedCommandSequence = Math.max(acknowledgedCommandSequence, sequence);
            for (const inputSequence of pendingInputs.keys()) {
                if (inputSequence <= sequence) pendingInputs.delete(inputSequence);
            }
        },
        resolveClipboard(requestId, text) {
            const operation = clipboardOperations.get(requestId);
            if (!operation || operation.finished) return;
            if (text === null) {
                operation.reject(new DOMException('No selection to copy.', 'AbortError'));
                finishClipboard(operation, false);
            } else {
                operation.hasText = true;
                operation.resolve(text);
            }
        },
        rememberSelection,
        projectedSelection: () => modelSelectionOnly ? knownSelection : null,
        takeInput(sourceBoundary) {
            if (!sourceBoundary) {
                const prefix = `input:${cellId}:`;
                return JSON.stringify(takeEventBatch(prefix).map(input => input.slice(prefix.length)));
            }
            for (const operation of [...clipboardOperations.values()]) {
                if (operation.cutSequence) finishClipboard(operation, false);
            }
            if (nativeEditSnapshot) {
                if (composing && nativeEditSnapshot.data?.length) {
                    nativeEditSnapshot.replacesSelection = true;
                }
                cancelNativeCommit();
                nativeCommitGeneration++;
                composing = false;
                compositionEnding = false;
                surfaceObservers.get(surface).composing = false;
                reconcileNativeEdit();
            }
            return JSON.stringify([...pendingInputs.values()]);
        },
    };
    const detachCommands = attachCommandSurface(surface, () => {
        if (nativeEditSnapshot && !composing) {
            compositionEnding = false;
            reconcileNativeEdit();
        }
    });

    function finishClipboard(operation, success) {
        if (operation.finished) return;
        operation.finished = true;
        operation.reject(new DOMException('Clipboard operation ended.', 'AbortError'));
        if (operation.cutSequence) {
            const sequence = operation.cutSequence;
            const input = success && operation.hasText
                ? `${sequence}:text:${sequence}:-1:-1:-1:-1:`
                : `${sequence}:noop:`;
            pendingInputs.set(sequence, input);
            resolveBarrier(`${cellId}/${operation.requestId}`, `input:${cellId}:${input}`);
        }
        clipboardOperations.delete(operation.requestId);
        if (!disposed) {
            surface.dataset.editorClipboardPending = String(clipboardOperations.size > 0);
        }
    }

    function copySelection(event, cut) {
        if (disposed || editorSurfaceHandlers.get(surface) !== handlers
            || document.activeElement !== surface || composing || compositionEnding) {
            return;
        }
        event.preventDefault();
        if (!navigator.clipboard?.write || typeof ClipboardItem !== 'function') {
            console.error('Clipboard writing is unavailable. The source was preserved.');
            return;
        }
        if (nativeEditSnapshot) {
            reconcileNativeEdit();
        }
        reportSelection();
        const requestId = commandSequence + 1;
        const operation = { requestId, cutSequence: 0, hasText: false, finished: false };
        const data = new Promise((resolve, reject) => {
            operation.resolve = resolve;
            operation.reject = reject;
        }).then(text => new Blob([text], { type: 'text/plain' }));
        data.catch(() => {});
        clipboardOperations.set(requestId, operation);
        surface.dataset.editorClipboardPending = 'true';
        emitInput('clipboard', String(requestId), cut ? `${cellId}/${requestId}` : null);
        if (cut) {
            operation.cutSequence = ++commandSequence;
            pendingInputs.set(commandSequence, null);
        }
        const failed = error => {
            if (!disposed && error?.name !== 'AbortError') console.error('Clipboard writing failed. The source was preserved.', error);
            finishClipboard(operation, false);
        };
        try {
            Promise.resolve(navigator.clipboard.write([new ClipboardItem({ 'text/plain': data })]))
                .then(() => finishClipboard(operation, true), failed);
        } catch (error) {
            failed(error);
        }
    }

    surface.addEventListener('copy', handlers.copy);
    surface.addEventListener('cut', handlers.cut);
    surface.addEventListener('beforeinput', handlers.beforeinput);
    surface.addEventListener('input', handlers.input);
    surface.addEventListener('paste', handlers.paste);
    surface.addEventListener('focus', handlers.focus);
    surface.addEventListener('pointerdown', handlers.pointerdown);
    surface.addEventListener('pointerup', handlers.pointerup);
    surface.addEventListener('keydown', handlers.keydown);
    surface.addEventListener('compositionstart', handlers.compositionstart);
    surface.addEventListener('compositionend', handlers.compositionend);
    document.addEventListener('selectionchange', handlers.selectionchange);
    surfaceObservers.set(surface, {
        observer, depth: 0, reconnectScheduled: false,
        pendingWrites: null, pendingSelection: null, pendingNodes: new Set(),
        composing: false, nativeEditPending: false, nativeEditGeneration: 0,
        nativeSequence: 0, nativeWindow: null,
    });
    observer.observe(surface, observeOptions);
    editorHandlers.set(cellId, handlers);
    editorSurfaceHandlers.set(surface, handlers);
}

export function detachEditorSurface(cellId, surface) {
    clearFindSelection(surface);
    const handlers = editorHandlers.get(cellId);
    if (!handlers) {
        return;
    }
    const selectionUpdate = editorSelectionUpdates.get(surface);
    if (selectionUpdate) {
        cancelAnimationFrame(selectionUpdate.frame);
        editorSelectionUpdates.delete(surface);
    }
    selectionSyncPending.delete(surface);
    handlers.dispose();
    surface.removeEventListener('copy', handlers.copy);
    surface.removeEventListener('cut', handlers.cut);
    surface.removeEventListener('beforeinput', handlers.beforeinput);
    surface.removeEventListener('input', handlers.input);
    surface.removeEventListener('paste', handlers.paste);
    surface.removeEventListener('focus', handlers.focus);
    surface.removeEventListener('pointerdown', handlers.pointerdown);
    surface.removeEventListener('pointerup', handlers.pointerup);
    surface.removeEventListener('keydown', handlers.keydown);
    surface.removeEventListener('compositionstart', handlers.compositionstart);
    surface.removeEventListener('compositionend', handlers.compositionend);
    document.removeEventListener('selectionchange', handlers.selectionchange);
    editorHandlers.delete(cellId);
    editorSurfaceHandlers.delete(surface);
}

export function acknowledgeEditorCommand(cellId, sequence) {
    editorHandlers.get(cellId)?.acknowledge(sequence);
}

export function resolveEditorClipboard(cellId, requestId, text) {
    editorHandlers.get(cellId)?.resolveClipboard(requestId, text);
}

export function setEditorSelection(
    surface,
    anchorLineIndex,
    anchorColumn,
    headLineIndex,
    headColumn,
    reveal = false,
) {
    const state = surfaceObservers.get(surface);
    if (state?.pendingWrites) {
        state.pendingSelection = [anchorLineIndex, anchorColumn, headLineIndex, headColumn, reveal];
        return;
    }
    const previous = editorSelectionUpdates.get(surface);
    if (previous) {
        cancelAnimationFrame(previous.frame);
        editorSelectionUpdates.delete(surface);
    }
    const update = {
        anchorLineIndex,
        anchorColumn,
        headLineIndex,
        headColumn,
        reveal,
        frame: 0,
    };
    if (!applyEditorSelection(surface, update)) {
        scheduleEditorSelection(surface, update);
    }
}

function scheduleEditorSelection(surface, update) {
    update.frame = requestAnimationFrame(() => {
        if (editorSelectionUpdates.get(surface) !== update) {
            return;
        }
        editorSelectionUpdates.delete(surface);
        if (!applyEditorSelection(surface, update)) {
            scheduleEditorSelection(surface, update);
        }
    });
    editorSelectionUpdates.set(surface, update);
}

function applyEditorSelection(surface, update) {
    const { anchorLineIndex, anchorColumn, headLineIndex, headColumn } = update;
    clearFindSelection(surface);
    const findPreview = surface.dataset.editorFindMatch === 'true'
        && document.activeElement?.closest?.('[data-editor-find]')?.closest('.editor-container') === surface.closest('.editor-container');
    const ownsNativeSelection = document.activeElement === surface
        && surface.contains(window.getSelection()?.anchorNode);
    ignoreOwnWrites(() => clearCaretMarkers(surface, false), surface);
    let selectionRows = null;
    const physicalLine = index => surface.querySelector(`[data-ln="${index}"]`);
    if (update.reveal && (document.activeElement === surface || findPreview)) {
        pinEditorLines(surface, headLineIndex);
    }
    const findLine = index => selectionRows ? selectionRows.find(row => Number(row.dataset.ln) === index) : physicalLine(index);
    const pointIn = (line, column) => {
        const lineLength = getEditorText(line).length;
        if (column > lineLength) {
            return null;
        }
        if (column === 0 || column === lineLength) {
            const position = column === 0 ? 'start' : 'end';
            let marker = line.querySelector(`[data-editor-caret-marker="${position}"]`);
            if (!marker) {
                ignoreOwnWrites(() => {
                    marker = document.createElement('span');
                    marker.dataset.editorCaretMarker = position;
                    marker.textContent = caretMarkerText;
                    if (position === 'start') {
                        line.prepend(marker);
                    } else {
                        line.append(marker);
                    }
                }, line);
            }
            return {
                node: marker.firstChild,
                offset: position === 'start' ? caretMarkerText.length : 0,
            };
        }

        let remaining = column;
        const walker = document.createTreeWalker(line, NodeFilter.SHOW_TEXT);
        let node = walker.nextNode();
        let last = null;
        while (node) {
            if (node.parentElement?.closest?.(caretMarkerSelector)) {
                node = walker.nextNode();
                continue;
            }
            if (remaining <= node.nodeValue.length) {
                return { node, offset: remaining };
            }
            remaining -= node.nodeValue.length;
            last = node;
            node = walker.nextNode();
        }
        return last ? { node: last, offset: last.nodeValue.length } : { node: line, offset: 0 };
    };

    if (anchorLineIndex !== headLineIndex && !findPreview) {
        if (update.reveal && document.activeElement === surface) {
            const head = physicalLine(headLineIndex);
            const point = head && pointIn(head, headColumn);
            if (point) {
                const range = document.createRange();
                range.setStart(point.node, point.offset);
                range.collapse(true);
                const rect = range.getBoundingClientRect(), scroll = surface.parentElement;
                const top = scroll.getBoundingClientRect().top + scroll.clientTop;
                if (rect.top < top) scroll.scrollTop += rect.top - top;
                else if (rect.bottom > top + scroll.clientHeight) scroll.scrollTop += rect.bottom - top - scroll.clientHeight;
            }
        }
        pinEditorLines(surface);
        selectionRows = [...surface.querySelectorAll(':scope > .editor-chunk')]
            .filter(chunk => chunk.style.contentVisibility === 'visible')
            .flatMap(chunk => [...chunk.querySelectorAll('.editor-line[data-ln]')]);
    }
    let anchorLine = findLine(anchorLineIndex);
    let headLine = findLine(headLineIndex);
    editorSurfaceHandlers.get(surface)?.rememberSelection(update, Boolean(anchorLine && headLine));
    if (document.activeElement !== surface) {
        if (findPreview && anchorLine && headLine) {
            const anchor = pointIn(anchorLine, anchorColumn), head = pointIn(headLine, headColumn);
            if (anchor && head) {
                if (update.reveal) revealEditorPoint(surface, head);
                if (globalThis.CSS?.highlights && globalThis.Highlight) {
                    const range = document.createRange();
                    const backwards = anchorLineIndex > headLineIndex || anchorLineIndex === headLineIndex && anchorColumn > headColumn;
                    const start = backwards ? head : anchor, end = backwards ? anchor : head;
                    range.setStart(start.node, start.offset); range.setEnd(end.node, end.offset);
                    const highlights = CSS.highlights.get('sia-editor-find') ?? new Highlight();
                    highlights.add(range); CSS.highlights.set('sia-editor-find', highlights);
                    findSelectionRanges.set(surface, range);
                }
            }
        }
        selectionSyncPending.delete(surface);
        return true;
    }
    let visibleAnchorColumn = anchorColumn, visibleHeadColumn = headColumn;
    if (!anchorLine || !headLine) {
        const rows = selectionRows ?? surface.querySelectorAll(':scope .editor-line[data-ln]');
        if (!rows.length) {
            return true;
        }
        const clampPoint = (index, column) => index < Number(rows[0].dataset.ln)
            ? { line: rows[0], column: 0 }
            : index > Number(rows[rows.length - 1].dataset.ln)
                ? { line: rows[rows.length - 1], column: getEditorText(rows[rows.length - 1]).length }
                : { line: findLine(index), column };
        const a = clampPoint(anchorLineIndex, anchorColumn), h = clampPoint(headLineIndex, headColumn);
        if (!a.line || !h.line || a.line === h.line && a.column === h.column) {
            const selection = window.getSelection();
            if (selection && (ownsNativeSelection || surface.contains(selection.anchorNode))) {
                selectionSyncPending.add(surface);
                selection.removeAllRanges();
            }
            return true;
        }
        anchorLine = a.line;
        headLine = h.line;
        visibleAnchorColumn = a.column;
        visibleHeadColumn = h.column;
    }
    const anchor = pointIn(anchorLine, visibleAnchorColumn);
    const head = pointIn(headLine, visibleHeadColumn);
    if (!anchor || !head) {
        return false;
    }
    const selection = window.getSelection();
    selectionSyncPending.add(surface);
    selection.setBaseAndExtent(anchor.node, anchor.offset, head.node, head.offset);
    if (update.reveal) revealEditorPoint(surface, head);
    return true;
}

function revealEditorPoint(surface, head) {
    const range = document.createRange();
    range.setStart(head.node, head.offset); range.collapse(true);
    const rect = range.getBoundingClientRect(), scroll = surface.parentElement;
    const box = scroll.getBoundingClientRect(), top = box.top + scroll.clientTop, left = box.left + scroll.clientLeft;
    if (rect.top < top) scroll.scrollTop += rect.top - top;
    else if (rect.bottom > top + scroll.clientHeight) scroll.scrollTop += rect.bottom - top - scroll.clientHeight;
    if (rect.left < left) scroll.scrollLeft += rect.left - left;
    else if (rect.right > left + scroll.clientWidth) scroll.scrollLeft += rect.right - left - scroll.clientWidth;
}

function clearFindSelection(surface) {
    const range = findSelectionRanges.get(surface);
    if (!range) return;
    findSelectionRanges.delete(surface);
    const highlights = globalThis.CSS?.highlights?.get('sia-editor-find');
    highlights?.delete(range);
    if (highlights?.size === 0) CSS.highlights.delete('sia-editor-find');
}

export function refreshEditorSelection(surface) {
    const state = surfaceObservers.get(surface);
    if (!state || document.activeElement !== surface || surface.dataset.editorUpdating === 'true'
        || state.pendingWrites || state.nativeEditPending || state.composing) return;
    const selection = editorSurfaceHandlers.get(surface)?.projectedSelection();
    if (!selection || selection.anchorLineIndex === selection.headLineIndex) return;
    setEditorSelection(surface, selection.anchorLineIndex, selection.anchorColumn,
        selection.headLineIndex, selection.headColumn);
}
