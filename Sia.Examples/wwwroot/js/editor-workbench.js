import { emit } from './notebook-runtime.js';
import { observeSoftKeyboard } from './editor-keyboard.js';

const workbenches = new WeakMap();

function notifyInput(root, action) {
    const binding = root.dataset.editorActiveBinding;
    if (binding) emit(`editor-page-input:${binding}:${action}`);
}

function attachViewport(root) {
    const body = document.body;
    const properties = ['--editor-viewport-height', '--editor-viewport-top'];
    const previous = properties.map(name => [name, body.style.getPropertyValue(name), body.style.getPropertyPriority(name)]);
    let reported = 'unknown';
    const keyboard = observeSoftKeyboard(sample => {
        const { offsetTop: top, visualHeight: height, rectangle: bounds } = sample;
        let bottom = top + height;
        root.dataset.editorKeyboardApi = String(sample.source === 'native');
        if (!sample.pending && sample.status !== 'unknown' && sample.status !== reported && root.dataset.editorActiveBinding) {
            const editing = document.activeElement?.matches('.editor-lines') && document.activeElement.closest('.editor-page') === root;
            if (sample.status === 'closed' || editing) {
                reported = sample.status;
                notifyInput(root, reported === 'open' ? 'keyboard-open' : 'keyboard-close');
            }
        }
        if (bounds?.width > 0 && bounds.height > 0 && bounds.top < bottom && bounds.top + bounds.height > top) {
            bottom = Math.max(top, bounds.top);
        }
        body.style.setProperty(properties[0], `${Math.max(0, bottom - top)}px`);
        body.style.setProperty(properties[1], `${top}px`);
    });
    return { schedule: keyboard.refresh, reset: keyboard.reset, dispose() {
        keyboard.dispose();
        for (const [name, value, priority] of previous) {
            if (value) body.style.setProperty(name, value, priority);
            else body.style.removeProperty(name);
        }
        delete root.dataset.editorKeyboardApi;
    } };
}

export function attachWorkbenchActions(surface, actions) {
    const root = surface.closest('.editor-page');
    const toolbar = root?.querySelector('.editor-page-touchbar');
    if (!toolbar || !actions) return { activate() {}, dispose() {} };
    let owner = workbenches.get(root);
    if (!owner) {
        const mobile = matchMedia('(any-pointer: coarse), (max-width: 720px)');
        const abort = new AbortController();
        owner = { bindings: new Map(), active: null, mobile, abort, viewport: attachViewport(root) };
        owner.project = () => {
            const connected = [...owner.bindings.keys()].filter(editor => editor.isConnected && !editor.closest('.tabs-only'));
            owner.active = connected.find(editor => editor === document.activeElement)
                ?? connected.find(editor => root.dataset.editorActiveBinding && editor.dataset.editorBinding === root.dataset.editorActiveBinding)
                ?? (connected.includes(owner.active) ? owner.active : null)
                ?? connected[0] ?? null;
            for (const [editor, binding] of owner.bindings) {
                const parent = mobile.matches && owner.active === editor ? toolbar : binding.home;
                if (binding.actions.parentElement !== parent) parent.prepend(binding.actions);
            }
        };
        owner.observer = new MutationObserver(records => {
            if (records.some(record => record.type === 'attributes'
                ? record.attributeName === 'data-editor-active-binding' || record.target.matches('.split-child')
                : record.target.matches('.editor-page-workbench, .group-content, .split-child, .cell-split'))) owner.project();
            if (records.some(record => record.type === 'attributes' && record.attributeName === 'data-editor-active-binding')) owner.viewport.schedule();
            if (records.some(record => record.type === 'attributes' && record.attributeName === 'data-editor-input-reset')) owner.viewport.reset();
        });
        owner.observer.observe(root, { childList: true, subtree: true, attributes: true, attributeFilter: ['data-editor-active-binding', 'data-editor-input-reset', 'class'] });
        document.addEventListener('focusin', event => {
            if (!root.classList.contains('input-tools-open')) return;
            if (event.target.closest('.editor-lines, .editor-page-touchbar, .menu-toggle, .menu-popover')) return;
            notifyInput(root, 'dismiss');
        }, { signal: abort.signal });
        mobile.addEventListener('change', owner.project, { signal: abort.signal });
        toolbar.addEventListener('pointerdown', event => {
            if (event.isPrimary !== false && event.button === 0 && event.target.closest('button')) event.preventDefault();
        }, { signal: abort.signal });
        workbenches.set(root, owner);
    }
    owner.bindings.set(surface, { actions, home: actions.parentElement });
    if (!owner.active) owner.active = surface;
    owner.project();
    return {
        activate() {
            owner.active = surface;
            owner.project();
        },
        dispose() {
            const binding = owner.bindings.get(surface);
            if (!binding) return;
            if (actions.parentElement !== binding.home) binding.home.prepend(actions);
            owner.bindings.delete(surface);
            if (owner.active === surface) owner.active = owner.bindings.keys().next().value ?? null;
            owner.project();
            if (!owner.bindings.size) {
                owner.abort.abort();
                owner.observer.disconnect();
                workbenches.delete(root);
                owner.viewport.dispose();
            }
        },
    };
}
