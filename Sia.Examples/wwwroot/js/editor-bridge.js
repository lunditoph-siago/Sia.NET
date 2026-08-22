import { emit } from './notebook-runtime.js';

const editorHandlers = new Map();
const selectionSyncPending = new WeakSet();
const editorSelectionUpdates = new WeakMap();
const editorSurfaceHandlers = new WeakMap();
const lastScrolledIntoView = new WeakMap();
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
    const lines = surface.querySelectorAll(':scope > .editor-line[data-ln]');
    if (!lines.length) {
        return null;
    }
    return {
        start: Number(lines[0].dataset.ln),
        end: Number(lines[lines.length - 1].dataset.ln) + 1,
    };
}

function columnInLine(line, node, offset) {
    const range = document.createRange();
    range.setStart(line, 0);
    range.setEnd(node, offset);
    return range.toString().replaceAll(caretMarkerText, '').length;
}

function clearCaretMarkers(surface, preserveSelection) {
    const markers = [...surface.querySelectorAll(caretMarkerSelector)];
    if (!preserveSelection) {
        markers.forEach((marker) => marker.remove());
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
        return {
            marker,
            offset: range.toString().replaceAll(caretMarkerText, '').length,
        };
    };
    const anchor = pointInMarker(selection?.anchorNode, selection?.anchorOffset ?? 0);
    const head = pointInMarker(selection?.focusNode, selection?.focusOffset ?? 0);
    const replacements = new Map();
    for (const marker of markers) {
        const text = marker.textContent.replaceAll(caretMarkerText, '');
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

function resolveSurface(node) {
    const element = node?.nodeType === Node.ELEMENT_NODE ? node : node?.parentElement;
    return element?.closest?.('.editor-lines') ?? null;
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
                if (state.depth === 0) {
                    state.observer.observe(surface, observeOptions);
                }
            });
        }
    }
}

export function attachEditorSurface(cellId, surface) {
    if (editorHandlers.has(cellId)) {
        throw new Error(`Editor '${cellId}' is already attached.`);
    }

    let nativeEditSnapshot = null;
    let knownSelection;
    let composing = false;
    let compositionEnding = false;
    let compositionGeneration = 0;
    let compositionCommitTimer;
    let commandSequence = 0;
    let acknowledgedCommandSequence = 0;

    const specialKeys = new Set([
        'Escape',
        'Tab',
        'ArrowUp',
        'ArrowDown',
        'ArrowLeft',
        'ArrowRight',
        'Home',
        'End',
        'PageUp',
        'PageDown',
        'Enter',
        'Backspace',
        'Delete',
    ]);
    const controlKeys = new Set([
        'a',
        'k',
        'ArrowLeft',
        'ArrowRight',
        'Home',
        'End',
        'Backspace',
        'Delete',
    ]);
    const hasPendingCommand = () => acknowledgedCommandSequence < commandSequence;
    const emitCommand = (eventType, selection, payload) => {
        commandSequence++;
        emit(
            `${eventType}:${cellId}:${commandSequence}:` +
                `${selectionArguments(selection)}:${payload}`,
        );
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
            anchorLine.parentNode !== surface ||
            headLine.parentNode !== surface
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
            value.replaceAll(caretMarkerText, '').replaceAll('\r\n', '\n').replaceAll('\r', '\n');
        const topLevelPoint = (node, offset) => {
            let top = node;
            if (top === surface) {
                if (offset === 0) {
                    return { line: null, lineIndex: 0, column: 0 };
                }
                top = surface.childNodes[Math.min(offset, surface.childNodes.length) - 1];
                node = top;
                offset =
                    top.nodeType === Node.TEXT_NODE ? top.nodeValue.length : top.childNodes.length;
            } else {
                while (top?.parentNode && top.parentNode !== surface) {
                    top = top.parentNode;
                }
            }
            if (!top || top.parentNode !== surface) {
                return null;
            }

            const range = document.createRange();
            if (isRealLine(top)) {
                range.setStart(top, 0);
                range.setEnd(node, offset);
                const lines = normalizeText(range.toString()).split('\n');
                return {
                    line: top,
                    lineIndex: Number(top.dataset.ln) + lines.length - 1,
                    column: lines.at(-1).length,
                };
            }

            let anchor = top.previousSibling;
            while (anchor && !isRealLine(anchor)) {
                anchor = anchor.previousSibling;
            }
            let lineIndex = anchor ? Number(anchor.dataset.ln) + 1 : 0;
            for (
                let sibling = anchor ? anchor.nextSibling : surface.firstChild;
                sibling && sibling !== top;
                sibling = sibling.nextSibling
            ) {
                const text = normalizeText(
                    sibling.nodeType === Node.TEXT_NODE
                        ? sibling.nodeValue
                        : (sibling.innerText ?? sibling.textContent ?? ''),
                );
                lineIndex += text.split('\n').length - 1;
            }
            range.setStart(top, 0);
            range.setEnd(node, offset);
            const lines = normalizeText(range.toString()).split('\n');
            return {
                line: null,
                lineIndex: lineIndex + lines.length - 1,
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
    const rememberSelection = (selection) => {
        knownSelection = selectionCoordinates(selection);
    };

    const emitSelection = () => {
        const selection = readSelection();
        if (!selection || sameSelection(selection, knownSelection)) {
            return;
        }
        rememberSelection(selection);
        emit(`sel:${cellId}:${selectionArguments(selection)}`);
    };

    const reportSelection = () => {
        if (
            selectionSyncPending.has(surface) ||
            nativeEditSnapshot ||
            composing ||
            compositionEnding
        ) {
            return;
        }
        emitSelection();
    };

    const readDocumentText = () =>
        [...surface.children]
            .filter((child) => isRealLine(child))
            .map((line) => line.textContent.replaceAll(caretMarkerText, ''))
            .join('\n');

    const armNativeEditSnapshot = (inputType, event) => {
        const selection = readSelection();
        if ((composing || compositionEnding) && nativeEditSnapshot) {
            nativeEditSnapshot.inputType = inputType;
            if (event?.data?.includes('\n') || event?.data?.includes('\r')) {
                nativeEditSnapshot.line = null;
            }
            return;
        }
        const changesStructure =
            inputType === 'insertParagraph' ||
            inputType === 'insertLineBreak' ||
            inputType === 'insertFromPaste' ||
            inputType === 'insertFromDrop' ||
            event?.data?.includes('\n') ||
            event?.data?.includes('\r');
        const line =
            !changesStructure && selection?.anchorLine === selection?.headLine
                ? selection.anchorLine
                : null;
        nativeEditSnapshot = {
            before: selectionCoordinates(selection) ?? knownSelection,
            line,
            inputType,
        };
    };

    const touchedLine = (records) => {
        let line;
        for (const record of records) {
            if (record.target === surface) {
                return undefined;
            }
            const recordLine = lineOf(surface, record.target);
            if (!recordLine || recordLine.parentNode !== surface) {
                return undefined;
            }
            if (line === undefined) {
                line = recordLine;
            } else if (line !== recordLine) {
                return undefined;
            }
        }
        return line;
    };

    const reconcileNativeEdit = (scope, line) => {
        clearCaretMarkers(surface, true);
        const snapshot = nativeEditSnapshot;
        nativeEditSnapshot = null;
        const after =
            scope === 'd' ? (readSelectionByDomOrder() ?? readSelection()) : readSelection();
        rememberSelection(after);
        if (!snapshot) {
            return;
        }

        let lineIndex;
        let windowEnd;
        let replacement;
        if (scope === 'd') {
            const range = mountedLineRange(surface);
            lineIndex = range ? range.start : -1;
            windowEnd = range ? range.end : -1;
            replacement = readDocumentText();
        } else {
            lineIndex = Number(line.dataset.ln);
            windowEnd = lineIndex + 1;
            replacement = line.textContent.replaceAll(caretMarkerText, '');
        }
        emit(
            `mut:${cellId}:${scope}:${lineIndex}:${windowEnd}:` +
                `${selectionArguments(snapshot.before)}:` +
                `${selectionArguments(after)}:` +
                `${encodeURIComponent(snapshot.inputType)}:` +
                encodeURIComponent(replacement),
        );
    };

    const commitComposition = () => {
        if (!nativeEditSnapshot) {
            return;
        }
        const { line } = nativeEditSnapshot;
        reconcileNativeEdit(line?.parentNode === surface ? 'l' : 'd', line);
    };

    const observer = new MutationObserver((records) => {
        if (composing || !nativeEditSnapshot) {
            return;
        }
        if (compositionEnding) {
            scheduleCompositionCommit();
            return;
        }
        const line = touchedLine(records);
        reconcileNativeEdit(line === undefined ? 'd' : 'l', line);
    });

    const cancelCompositionCommit = () => {
        if (compositionCommitTimer !== undefined) {
            clearTimeout(compositionCommitTimer);
            compositionCommitTimer = undefined;
        }
    };

    const scheduleCompositionCommit = () => {
        cancelCompositionCommit();
        const generation = ++compositionGeneration;
        compositionCommitTimer = setTimeout(() => {
            compositionCommitTimer = undefined;
            if (composing || generation !== compositionGeneration) {
                return;
            }
            compositionEnding = false;
            commitComposition();
        }, 20);
    };

    const commandSelection = () => (hasPendingCommand() ? null : readSelection());
    const emitKeyCommand = (key, control, shift, alt) =>
        emitCommand('key', commandSelection(), `${key}:${control}:${shift}:${alt}`);
    const emitTextCommand = (text) =>
        emitCommand('text', commandSelection(), encodeURIComponent(text));
    const emitInputCommand = (event, command) => {
        event.preventDefault();
        nativeEditSnapshot = null;
        command();
    };

    const handlers = {
        focus() {
            emit(`editor-focus:${cellId}`);
        },
        beforeinput(event) {
            selectionSyncPending.delete(surface);
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
                emitInputCommand(event, () => emitKeyCommand('Enter', false, false, false));
                return;
            }
            if (!composing && !compositionEnding && event.inputType === 'deleteContentBackward') {
                emitInputCommand(event, () => emitKeyCommand('Backspace', false, false, false));
                return;
            }
            if (!composing && !compositionEnding && event.inputType === 'deleteWordBackward') {
                emitInputCommand(event, () => emitKeyCommand('Backspace', true, false, false));
                return;
            }
            if (!composing && !compositionEnding && event.inputType === 'deleteContentForward') {
                emitInputCommand(event, () => emitKeyCommand('Delete', false, false, false));
                return;
            }
            if (!composing && !compositionEnding && event.inputType === 'deleteWordForward') {
                emitInputCommand(event, () => emitKeyCommand('Delete', true, false, false));
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
                        emitTextCommand(text.replaceAll('\r\n', '\n').replaceAll('\r', '\n')),
                    );
                    return;
                }
            }
            armNativeEditSnapshot(event.inputType || 'insertText', event);
        },
        pointerdown() {
            selectionSyncPending.delete(surface);
        },
        pointerup: reportSelection,
        selectionchange() {
            if (selectionSyncPending.has(surface)) {
                selectionSyncPending.delete(surface);
                return;
            }
            reportSelection();
        },
        keydown(event) {
            selectionSyncPending.delete(surface);
            if (event.isComposing || composing || compositionEnding) {
                return;
            }
            const control = (event.ctrlKey || event.metaKey) && !event.altKey;
            const key = control && event.key.length === 1 ? event.key.toLowerCase() : event.key;
            const handlesKey = control ? controlKeys.has(key) : specialKeys.has(key);
            if (handlesKey) {
                event.preventDefault();
                emitKeyCommand(key, event.ctrlKey || event.metaKey, event.shiftKey, event.altKey);
            }
        },
        compositionstart(event) {
            selectionSyncPending.delete(surface);
            cancelCompositionCommit();
            composing = true;
            compositionEnding = false;
            compositionGeneration++;
            armNativeEditSnapshot('insertCompositionText', event);
        },
        compositionend() {
            composing = false;
            compositionEnding = true;
            scheduleCompositionCommit();
        },
        dispose() {
            compositionGeneration++;
            cancelCompositionCommit();
            observer.disconnect();
            surfaceObservers.delete(surface);
        },
        acknowledge(sequence) {
            acknowledgedCommandSequence = Math.max(acknowledgedCommandSequence, sequence);
        },
        rememberSelection,
    };

    surface.addEventListener('beforeinput', handlers.beforeinput);
    surface.addEventListener('focus', handlers.focus);
    surface.addEventListener('pointerdown', handlers.pointerdown);
    surface.addEventListener('pointerup', handlers.pointerup);
    surface.addEventListener('keydown', handlers.keydown);
    surface.addEventListener('compositionstart', handlers.compositionstart);
    surface.addEventListener('compositionend', handlers.compositionend);
    document.addEventListener('selectionchange', handlers.selectionchange);
    surfaceObservers.set(surface, { observer, depth: 0, reconnectScheduled: false });
    observer.observe(surface, observeOptions);
    editorHandlers.set(cellId, handlers);
    editorSurfaceHandlers.set(surface, handlers);
}

export function detachEditorSurface(cellId, surface) {
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
    surface.removeEventListener('beforeinput', handlers.beforeinput);
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

export function setEditorSelection(
    surface,
    anchorLineIndex,
    anchorColumn,
    headLineIndex,
    headColumn,
) {
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
    ignoreOwnWrites(() => clearCaretMarkers(surface, false), surface);
    const findLine = (index) => surface.querySelector(`[data-ln="${index}"]`);
    const pointIn = (line, column) => {
        const lineLength = line.textContent.replaceAll(caretMarkerText, '').length;
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

    const anchorLine = findLine(anchorLineIndex);
    const headLine = findLine(headLineIndex);
    if (!anchorLine || !headLine) {
        selectionSyncPending.delete(surface);
        return true;
    }
    editorSurfaceHandlers.get(surface)?.rememberSelection(update);
    if (document.activeElement !== surface) {
        selectionSyncPending.delete(surface);
        return true;
    }
    const anchor = pointIn(anchorLine, anchorColumn);
    const head = pointIn(headLine, headColumn);
    if (!anchor || !head) {
        return false;
    }
    const selection = window.getSelection();
    selectionSyncPending.add(surface);
    selection.setBaseAndExtent(anchor.node, anchor.offset, head.node, head.offset);
    const key = `${anchorLineIndex}:${anchorColumn}:${headLineIndex}:${headColumn}`;
    if (lastScrolledIntoView.get(surface) !== key) {
        lastScrolledIntoView.set(surface, key);
        headLine.scrollIntoView({ block: 'nearest', inline: 'nearest' });
    }
    return true;
}
