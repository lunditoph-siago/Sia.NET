import { dotnet } from './_framework/dotnet.js'

document.getElementById('sidebar-toggle')?.addEventListener('click', () => {
    document.getElementById('app')?.classList.toggle('sidebar-open');
});

document.getElementById('packages-toggle')?.addEventListener('click', (e) => {
    e.stopPropagation();
    document.getElementById('header-packages')?.classList.toggle('open');
});
document.addEventListener('click', (e) => {
    const container = document.getElementById('header-packages');
    if (container?.classList.contains('open') && !container.contains(e.target)) {
        container.classList.remove('open');
    }
});

console.log('[diag] crossOriginIsolated=' + self.crossOriginIsolated);
console.log('[diag] calling dotnet.create()...');
const { setModuleImports, runMain, getConfig, getAssemblyExports } = await dotnet.create();
console.log('[diag] dotnet.create() resolved');

const events = [];
const listeners = [];
function emit(payload) {
    if (listeners.length > 0) { listeners.shift()(payload); }
    else { events.push(payload); }
}

const editorEvents = [];
const editorListeners = [];
function emitEditor(payload) {
    if (editorListeners.length > 0) { editorListeners.shift()(payload); }
    else { editorEvents.push(payload); }
}

const editorHandlers = {};

setModuleImports('main.js', {
    find(id)                    { return document.getElementById(id); },
    create(tag)                 { return document.createElement(tag); },
    createText(value)           { return document.createTextNode(value); },
    remove(element)             { element?.remove(); },
    insertBefore(parent, child, before) { parent?.insertBefore(child, before); },

    setText(element, value)     { if (element) element.textContent = value; },
    getText(element)            { return element?.textContent ?? ''; },
    getValue(element)           { return element?.value ?? ''; },
    setId(element, id)          { if (element) element.id = id; },
    setCssText(element, css)   { if (element) element.style.cssText = css; },
    toggleClass(element, name, on) { element?.classList.toggle(name, on); },
    setAttr(element, name, value) { element?.setAttribute(name, value); },
    focus(element)              { element?.focus(); },

    attachEditorSurface(cellId, element) {
        if (editorHandlers[cellId]) return;
        const h = {};
        const special = ['Escape','Tab','ArrowUp','ArrowDown','ArrowLeft','ArrowRight',
                        'Home','End','PageUp','PageDown','Enter','Backspace','Delete'];

        const lineOf = (node) => {
            let n = node;
            while (n && n !== element) {
                if (n.nodeType === 1 && n.dataset && n.dataset.ln !== undefined) return n;
                n = n.parentNode;
            }
            return null;
        };
        const colInLine = (lineEl, node, offset) => {
            const r = document.createRange();
            r.setStart(lineEl, 0);
            r.setEnd(node, offset);
            return r.toString().length;
        };
        const reportSelection = () => {
            const sel = window.getSelection();
            if (!sel || sel.rangeCount === 0 || !element.contains(sel.anchorNode)) return;
            const aLine = lineOf(sel.anchorNode), fLine = lineOf(sel.focusNode);
            if (!aLine || !fLine) return;
            const aCol = colInLine(aLine, sel.anchorNode, sel.anchorOffset);
            const fCol = colInLine(fLine, sel.focusNode, sel.focusOffset);
            emitEditor(`sel:${cellId}:${aLine.dataset.ln}:${aCol}:${fLine.dataset.ln}:${fCol}`);
        };
        const reportMutation = () => {
            const sel = window.getSelection();
            const line = sel && sel.anchorNode ? lineOf(sel.anchorNode) : null;
            if (line && line.parentNode === element) {
                emitEditor(`mutline:${cellId}:${line.dataset.ln}\0${line.textContent}`);
            } else {
                emitEditor(`mutall:${cellId}:\0${element.innerText}`);
            }
        };

        h.keydown = e => {
            if (e.isComposing) return;
            const ctrlOnly = (e.ctrlKey || e.metaKey) && !e.altKey;
            if (special.includes(e.key) || ctrlOnly) {
                e.preventDefault();
                emitEditor(`key:${cellId}:${e.key}:${e.ctrlKey || e.metaKey}:${e.shiftKey}:${e.altKey}`);
            }
        };
        h.input = e => {
            if (e.isComposing) return;
            reportMutation();
            reportSelection();
        };
        h.compositionend = () => {
            reportMutation();
            reportSelection();
        };
        h.selectionchange = () => reportSelection();

        element.addEventListener('keydown', h.keydown);
        element.addEventListener('input', h.input);
        element.addEventListener('compositionend', h.compositionend);
        document.addEventListener('selectionchange', h.selectionchange);
        editorHandlers[cellId] = h;
    },
    detachEditorSurface(cellId, element) {
        const h = editorHandlers[cellId];
        if (!h) return;
        element.removeEventListener('keydown', h.keydown);
        element.removeEventListener('input', h.input);
        element.removeEventListener('compositionend', h.compositionend);
        document.removeEventListener('selectionchange', h.selectionchange);
        delete editorHandlers[cellId];
    },
    setEditorSelection(element, anchorLine, anchorCol, headLine, headCol) {
        const findLine = (ln) => element.querySelector(`[data-ln="${ln}"]`);
        const pointIn = (lineEl, col) => {
            let remaining = col;
            const walker = document.createTreeWalker(lineEl, NodeFilter.SHOW_TEXT);
            let node = walker.nextNode();
            let last = null;
            while (node) {
                const len = node.nodeValue.length;
                if (remaining <= len) return { node, offset: remaining };
                remaining -= len;
                last = node;
                node = walker.nextNode();
            }
            return last ? { node: last, offset: last.nodeValue.length } : { node: lineEl, offset: 0 };
        };
        const aLine = findLine(anchorLine), hLine = findLine(headLine);
        if (!aLine || !hLine) return;
        const a = pointIn(aLine, anchorCol), h = pointIn(hLine, headCol);
        const sel = window.getSelection();
        if (sel.setBaseAndExtent) sel.setBaseAndExtent(a.node, a.offset, h.node, h.offset);
        hLine.scrollIntoView({ block: 'nearest', inline: 'nearest' });
    },
    syncGutterScroll(scrollEl, gutterEl) {
        if (!scrollEl || !gutterEl) return;
        scrollEl.addEventListener('scroll', () => { gutterEl.scrollTop = scrollEl.scrollTop; });
    },

    listen(element, name, payload) { element?.addEventListener(name, () => emit(payload)); },

    waitForEvent() {
        if (events.length > 0) return Promise.resolve(events.shift());
        return new Promise(resolve => listeners.push(resolve));
    },
    waitForEditorEvent() {
        if (editorEvents.length > 0) return Promise.resolve(editorEvents.shift());
        return new Promise(resolve => editorListeners.push(resolve));
    },
    setInnerHtml(element, html) { if (element) element.innerHTML = html; },
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
        .map(asset => asset.resolvedUrl).filter(Boolean)
        .map(url => new URL(url, document.baseURI).href);
    const exports = await getAssemblyExports(config.mainAssemblyName);
    await exports.Sia_Examples.Notebook.BrowserNotebookInterop.InitNotebookAsync(urls);
}

try {
    console.log('[diag] calling initNotebook()...');
    await initNotebook();
    console.log('[diag] initNotebook() resolved');
} catch (err) {
    console.error('Failed to initialize notebook runtime:', err);
    showBootError('Some example dependencies failed to load — notebooks may fail to compile. Try reloading the page.');
}
console.log('[diag] calling runMain()...');
await runMain();
console.log('[diag] runMain() returned');
