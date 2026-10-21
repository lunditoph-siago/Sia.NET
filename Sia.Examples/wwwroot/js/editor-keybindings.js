import { emit } from './notebook-runtime.js';

const surfaces = new WeakMap();
let app;
const primaryIsMeta = /Mac|iPhone|iPad/.test(navigator.platform);
let projection = '';
let bindings = [];
let chord;
let chordTimer;
let lastEditor;
let dialog;
let returnFocus;

function clearChord(message = '') {
    chord = undefined;
    clearTimeout(chordTimer);
    const status = document.getElementById('keymap-chord');
    if (status) status.textContent = message;
}

function active(binding, event, surface) {
    if (binding.mode && binding.mode !== 'Standard'
        && event.target.closest?.('.editor-lines[data-editor-binding]') !== surface) return false;
    switch (binding.when) {
        case 'Workbench': return true;
        case 'Editor': return Boolean(surface);
        case 'EditorLab': return Boolean(document.querySelector('.editor-page'));
        case 'Notebook': {
            const script = event.target.closest?.('[data-script-id]');
            return Boolean(script && (binding.command !== 'NotebookSave' || script.dataset.scriptEditable === 'true'));
        }
        case 'Suggestions': return Boolean(surface?.getAttribute('aria-controls'));
        case 'Signatures': return Boolean(surface?.getAttribute('aria-describedby'));
        default: return false;
    }
}

function matches(stroke, event, mode) {
    const key = event.key === ' ' ? 'Space' : event.key.length === 1 ? event.key.toUpperCase() : event.key;
    const physical = `[${event.code.toUpperCase()}]`;
    let modifiers = stroke.modifiers;
    if (modifiers & 16) modifiers = modifiers & ~16 | (primaryIsMeta ? 2 : 1);
    let actual = (event.ctrlKey ? 1 : 0) | (event.metaKey ? 2 : 0)
        | (event.altKey ? 4 : 0) | (event.shiftKey ? 8 : 0);
    if ((mode === 'Normal' || mode === 'Visual') && event.key.length === 1
        && !(actual & ~8) && !stroke.key.startsWith('[')) {
        actual = event.key !== event.key.toLowerCase() ? 8 : 0;
    }
    return modifiers === actual && (stroke.key === key || stroke.key === physical);
}

export function resolveShortcut(event, surface) {
    if (event.isComposing || event.keyCode === 229 || event.getModifierState?.('AltGraph')
        || dialog?.isConnected || !app?.isConnected) return null;
    const value = app.dataset.editorKeymap ?? '';
    if (value !== projection) {
        projection = value;
        bindings = value ? JSON.parse(value) : [];
        clearChord();
    }
    if (['Control', 'Meta', 'Shift', 'Alt'].includes(event.key)) return null;
    if (chord) {
        if (event.repeat) return { pending: true };
        const previous = chord;
        clearChord();
        if (event.key === 'Escape') return { pending: true };
        if (previous.surface === surface) {
            const completed = previous.bindings.filter(binding => active(binding, event, surface) && matches(binding.keys[1], event, binding.mode));
            if (completed.length) return { binding: completed[0] };
            clearChord('No command matches this key combination.');
            chordTimer = setTimeout(() => clearChord(), 1500);
            return { pending: true };
        }
    }
    const eligible = bindings.filter(binding => active(binding, event, surface) && matches(binding.keys[0], event, binding.mode))
        .sort((first, second) => second.priority - first.priority);
    if (!eligible.length) return null;
    const first = eligible[0];
    if (event.repeat && !first.editor) return { pending: true };
    if (first.keys.length > 1) {
        if (surface?.dataset.editorProfile === 'Vim' && first.mode
            && first.keys.every(stroke => stroke.key.length === 1 && !(stroke.modifiers & ~8))) return { pending: true };
        chord = { surface, bindings: eligible.filter(binding => binding.keys.length > 1) };
        const status = document.getElementById('keymap-chord');
        if (status) status.textContent = surface?.dataset?.editorProfile === 'Vim' ? '' : 'Waiting for the second key… Esc to cancel';
        chordTimer = setTimeout(() => clearChord(), 5000);
        return { pending: true };
    }
    return { binding: first, fallback: eligible.find(binding => binding.when !== 'Suggestions' && binding.keys.length === 1) };
}

export function attachCommandSurface(surface, flush) {
    surfaces.set(surface, flush);
    return () => {
        surfaces.delete(surface);
        if (lastEditor === surface) lastEditor = undefined;
        if (chord?.surface === surface) clearChord();
    };
}

export function cancelShortcut(surface) {
    if (!chord || chord.surface !== surface) return false;
    clearChord();
    return true;
}

export function invokeWorkbenchCommand(command, surface = lastEditor) {
    if (!surface?.isConnected) surface = undefined;
    if (surface) surfaces.get(surface)?.();
    emit(`workbench-command:${command}:${surface?.dataset.editorBinding ?? ''}`);
}

function panelEvent(action, value = '') {
    if (dialog?.dataset.editorDialog) {
        if (dialog.hasAttribute('data-editor-dialog-list') && (action === 'accept' || action === 'move'))
            value = `${dialog.dataset.editorDialogRevision}${value ? `:${value}` : ''}`;
        emit(`${dialog.dataset.editorDialog}:${dialog.dataset.editorDialogStamp}:${action}${value ? `:${value}` : ''}`);
        return;
    }
    if (dialog) emit(`keymap-${action}:${dialog.dataset.keymapGeneration}:${value}`);
}

document.addEventListener('focusin', event => {
    const surface = event.target.closest?.('.editor-lines[data-editor-binding]');
    if (surface) lastEditor = surface;
    if (chord && chord.surface !== surface) clearChord();
});
document.addEventListener('pointerdown', event => {
    if (!event.target.closest?.('[data-editor-touch-command="Escape"]')) clearChord();
}, true);
document.addEventListener('compositionstart', () => clearChord());
window.addEventListener('blur', () => clearChord());

document.addEventListener('click', event => {
    const findAction = event.target.closest?.('[data-editor-find-action]');
    if (findAction) {
        const find = findAction.closest('[data-editor-find]');
        if (find) {
            event.preventDefault();
            emit(`find:${find.dataset.editorFind}:${findAction.dataset.editorFindAction}:${find.dataset.editorFindRevision}`);
        }
        return;
    }
    const command = event.target.closest?.('[data-workbench-command]');
    if (!command) return;
    event.preventDefault();
    invokeWorkbenchCommand(command.dataset.workbenchCommand);
});

document.addEventListener('keydown', event => {
    if (event.defaultPrevented || event.isComposing || event.keyCode === 229) return;
    if (dialog?.isConnected) {
        const recorder = event.target.closest?.('[data-keymap-recording="true"]');
        if (recorder) {
            event.preventDefault();
            if (event.repeat) return;
            if (event.key === 'Escape') { panelEvent('record'); return; }
            if (['Control', 'Meta', 'Alt', 'Shift'].includes(event.key)) return;
            const key = event.key === ' ' ? 'Space' : event.key === '+' ? 'Plus'
                : event.key.length === 1 ? event.key.toUpperCase() : event.key;
            const mode = recorder.closest?.('.keymap-edit')?.querySelector('[data-keymap-mode]')?.value;
            const logical = ['Normal', 'Visual'].includes(mode) && event.key.length === 1 && !event.ctrlKey && !event.metaKey && !event.altKey;
            const shift = logical ? event.key !== event.key.toLowerCase() : event.shiftKey;
            const modifiers = [event.ctrlKey && 'Ctrl', event.metaKey && 'Cmd', event.altKey && 'Alt', shift && 'Shift'].filter(Boolean);
            const stroke = [...modifiers, key].join('+');
            const before = recorder.dataset.keymapRecordedId === recorder.dataset.keymapRecordId
                ? recorder.dataset.keymapRecorded ?? '' : '';
            recorder.value = before && !before.includes(' ') ? `${before} ${stroke}` : stroke;
            recorder.dataset.keymapRecorded = recorder.value;
            recorder.dataset.keymapRecordedId = recorder.dataset.keymapRecordId;
            return;
        }
        if (event.key === 'Escape') { event.preventDefault(); panelEvent('escape'); }
        else if (dialog.dataset.editorDialog && event.key === 'Enter' && event.target.matches('input')) {
            event.preventDefault(); panelEvent('accept');
        }
        else if (dialog.dataset.keymapDialog === 'commands'
            || dialog.hasAttribute('data-editor-dialog-list') && event.target.matches('input')) {
            if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
                event.preventDefault(); panelEvent('move', event.key === 'ArrowDown' ? '1' : '-1');
            } else if (event.key === 'Enter') { event.preventDefault(); panelEvent('accept'); }
        }
        return;
    }
    if (event.target.closest?.('.editor-lines[data-editor-binding]')) return;
    const find = event.target.closest?.('[data-editor-find]');
    if (find && (event.key === 'Enter' || event.key === 'Escape')) {
        event.preventDefault();
        const action = event.key === 'Escape' ? 'close' : event.shiftKey ? 'previous'
            : event.target.dataset.editorFindField === 'replacement' ? 'replace' : 'next';
        emit(`find:${find.dataset.editorFind}:${action}:${find.dataset.editorFindRevision}`);
        return;
    }
    const findSurface = find?.closest('.editor-container')?.querySelector('.editor-lines[data-editor-binding]');
    const result = resolveShortcut(event, findSurface);
    if (!result) return;
    if (find && event.target.matches('input') && result.binding?.editor
        && !['Find', 'Replace', 'NextMatch', 'PreviousMatch'].includes(result.binding.command)) return;
    event.preventDefault();
    if (result.binding) invokeWorkbenchCommand(result.binding.command,
        findSurface ?? event.target.closest?.('[data-script-id]')?.querySelector('.editor-lines[data-editor-binding]') ?? lastEditor);
});

const panelObserver = new MutationObserver(() => {
    if (dialog && !dialog.isConnected) {
        dialog = undefined;
        if (returnFocus?.isConnected) returnFocus.focus({ preventScroll: true });
        returnFocus = undefined;
    }
    const next = app.querySelector(':scope > dialog[data-keymap-dialog], :scope > dialog[data-editor-dialog]');
    if (!next || next === dialog) return;
    clearChord();
    returnFocus = document.activeElement;
    dialog = next;
    next.addEventListener('cancel', event => { event.preventDefault(); panelEvent('escape'); });
    next.showModal();
    const input = next.querySelector('[data-keymap-search], [data-editor-dialog-focus]');
    input?.focus();
    if (input?.hasAttribute('data-editor-dialog-select')) input.select();
});

function initialize() {
    app = document.getElementById('app');
    if (app) panelObserver.observe(app, { childList: true });
}

if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', initialize, { once: true });
else initialize();
