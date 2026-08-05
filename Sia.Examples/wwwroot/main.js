import { dotnet } from './_framework/dotnet.js'

document.getElementById('sidebar-toggle')?.addEventListener('click', () => {
    document.getElementById('app')?.classList.toggle('sidebar-open');
});

const { setModuleImports, runMain, getConfig, getAssemblyExports } = await dotnet.create();

const events = [];
const listeners = [];

const editorEvents = [];
const editorListeners = [];

function emit(payload) {
    if (listeners.length > 0) {
        listeners.shift()(payload);
    } else {
        events.push(payload);
    }
}

function emitEditor(payload) {
    if (editorListeners.length > 0) {
        editorListeners.shift()(payload);
    } else {
        editorEvents.push(payload);
    }
}

const editors = {};

function editorKeyDown(e) {
    const cellId = e.target.dataset.editorCellId;
    if (!cellId) return;

    const specialKeys = ['Escape', 'Tab', 'ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight',
                         'Home', 'End', 'PageUp', 'PageDown'];
    if (specialKeys.includes(e.key) || (e.ctrlKey && e.key === 's')) {
        e.preventDefault();
        emitEditor(`key:${cellId}:${e.key}:${e.ctrlKey}:${e.shiftKey}:${e.altKey}`);
    }
}

function editorInput(e) {
    const cellId = e.target.dataset.editorCellId;
    if (!cellId) return;
    emitEditor(`input:${cellId}:${e.target.value}`);
}

function editorScroll(e) {
    const cellId = e.target.dataset.editorCellId;
    if (!cellId) return;
    const editor = editors[cellId];
    if (editor && editor.pre) {
        editor.pre.scrollTop = e.target.scrollTop;
        editor.pre.scrollLeft = e.target.scrollLeft;
    }
}

setModuleImports('main.js', {
    find(id) {
        return document.getElementById(id);
    },
    create(tag) {
        return document.createElement(tag);
    },
    createText(value) {
        return document.createTextNode(value);
    },
    setText(element, value) {
        if (element?.textContent !== value) {
            element.textContent = value;
        }
    },
    getValue(element) {
        return element?.value ?? "";
    },
    setId(element, id) {
        if (element) element.id = id;
    },
    setPosition(element, top, left) {
        if (!element) return;
        element.style.position = 'fixed';
        element.style.top = top + 'px';
        element.style.left = left + 'px';
    },
    toggleClass(element, name, enabled) {
        element?.classList.toggle(name, enabled);
    },
    listen(element, name, payload) {
        element?.addEventListener(name, () => emit(payload));
    },
    insertBefore(parent, child, before) {
        parent?.insertBefore(child, before);
    },
    remove(element) {
        element?.remove();
    },
    waitForEvent() {
        if (events.length > 0) {
            return Promise.resolve(events.shift());
        }
        return new Promise(resolve => listeners.push(resolve));
    },
    waitForEditorEvent() {
        if (editorEvents.length > 0) {
            return Promise.resolve(editorEvents.shift());
        }
        return new Promise(resolve => editorListeners.push(resolve));
    },
    setInnerHtml(element, html) {
        if (element) element.innerHTML = html;
    },
    attachEditor(container, cellId, initialValue) {
        container.innerHTML = '';
        container.classList.add('editor-container');

        const editorId = 'editor-' + cellId;

        const gutter = document.createElement('div');
        gutter.className = 'editor-gutter';
        gutter.id = editorId + '-gutter';

        const wrap = document.createElement('div');
        wrap.className = 'editor-content-wrap';

        const pre = document.createElement('pre');
        pre.className = 'editor-content';
        pre.id = editorId + '-content';
        pre.setAttribute('aria-hidden', 'true');

        const textarea = document.createElement('textarea');
        textarea.className = 'editor-textarea';
        textarea.id = editorId + '-textarea';
        textarea.value = initialValue;
        textarea.spellcheck = false;
        textarea.autocorrect = 'off';
        textarea.autocapitalize = 'off';
        textarea.setAttribute('wrap', 'off');
        textarea.dataset.editorCellId = cellId;

        textarea.addEventListener('keydown', editorKeyDown);
        textarea.addEventListener('input', editorInput);
        textarea.addEventListener('scroll', editorScroll);

        wrap.appendChild(pre);
        wrap.appendChild(textarea);
        container.appendChild(gutter);
        container.appendChild(wrap);

        editors[cellId] = { textarea, pre, gutter };
    },
    detachEditor(container, cellId) {
        const editor = editors[cellId];
        if (editor) {
            editor.textarea.removeEventListener('keydown', editorKeyDown);
            editor.textarea.removeEventListener('input', editorInput);
            editor.textarea.removeEventListener('scroll', editorScroll);
            delete editors[cellId];
        }
        container.innerHTML = '';
        container.classList.remove('editor-container');
    },
    setEditorText(container, text) {
        const textarea = container.querySelector('textarea[data-editor-cell-id]');
        if (textarea && textarea.value !== text) {
            const start = textarea.selectionStart;
            const end = textarea.selectionEnd;
            textarea.value = text;
            textarea.selectionStart = Math.min(start, text.length);
            textarea.selectionEnd = Math.min(end, text.length);
        }
    },
});

function showBootError(message) {
    const banner = document.createElement('div');
    banner.className = 'boot-error-banner';
    banner.textContent = message;
    document.body.prepend(banner);
}

async function initNotebook() {
    const config = getConfig();
    const resources = config.resources ?? {};
    const urls = [...(resources.coreAssembly ?? []), ...(resources.assembly ?? [])]
        .map(asset => asset.resolvedUrl)
        .filter(Boolean)
        .map(url => new URL(url, document.baseURI).href);

    const exports = await getAssemblyExports(config.mainAssemblyName);
    await exports.Sia_Examples.Notebook.BrowserNotebookInterop.InitNotebookAsync(urls);
}

try {
    await initNotebook();
} catch (err) {
    console.error('Failed to initialize notebook runtime:', err);
    showBootError('Some example dependencies failed to load — notebooks may fail to compile. Try reloading the page.');
}
await runMain();
