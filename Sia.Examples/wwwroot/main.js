import { dotnet } from './_framework/dotnet.js';
import { configureRuntimeWorkers } from './js/runtime-workers.js';
import './js/app-shell.js';
import {
    createUniformInspector, disposeUniformInspector, createUniformBinding,
    configureUniformBinding, activateUniformBinding, readUniformEdits, disposeUniformBinding,
    readUniformImageEdits, readUniformImagePixels,
} from './js/uniform-inspector.js';
import {
    createPipelineCanvas,
    nextPipelineFrame,
    disposePipelineCanvas,
} from './js/pipeline-canvas.js';
import {
    createShaderPreview,
    setShaderPreviewRevision,
    showShaderPreview,
    disposeShaderPreview,
    downloadShaderBinary,
} from './js/shader-preview.js';
import { bootModuleConfig, dismissBootOverlay, showBootError } from './js/boot-progress.js';
import './js/cell-interactions.js';
import { renderEditorLines, setEditorActiveLine, clearEditorLines, disposeEditorLines } from './js/editor-lines.js';
import {
    acknowledgeEditorCommand,
    resolveEditorClipboard,
    attachEditorSurface,
    getEditorText,
    detachEditorSurface,
    writeEditorDom,
    prepareEditorReconciliation,
    takeEditorInput,
    setEditorUpdating,
    setEditorSelection,
} from './js/editor-bridge.js';
import {
    attachGutterHeights,
    detachGutterHeights,
    readEditorScrollPosition,
    restoreEditorScroll,
    cancelEditorScroll,
    restoreEditorFocus,
    setDocumentLines,
} from './js/editor-heightmap.js';
import { clearOverlayPlacement, ensureVisible, placeOverlay } from './js/editor-overlay.js';
import {
    cancelScheduledEvent,
    emit,
    fetchBase64,
    fetchText,
    notebookGetAllJson,
    notebookGetJson,
    notebookPut,
    notebookRemove,
    scheduleEvent,
    waitForEvent,
    workspaceGetAllJson,
    workspaceGetJson,
    workspacePut,
    workspaceRemove,
    workspaceRename,
} from './js/notebook-runtime.js';

dotnet.withConfig({ pthreadPoolInitialSize: 8, pthreadPoolUnusedSize: 0 });
dotnet.withModuleConfig?.(bootModuleConfig);
const runtime = await dotnet.create();
configureRuntimeWorkers(runtime);
const { setModuleImports, runMain, getConfig, getAssemblyExports } = runtime;

function applyDomBatch(handles, tags, values, writes, completingScope = null) {
    const kind = { text: 0, runs: 1, id: 2, attribute: 3, class: 4, listen: 5, insert: 6, remove: 7, selection: 8, editorLines: 9, activeEditorLine: 10, clearEditorLines: 11, disposeEditorLines: 12 };
    const created = Array.isArray(tags) ? tags.filter(tag => tag).map(() => null) : [];
    let failure = null;
    let completesScope = false;
    try {
        completesScope = completingScope?.nodeType === 1;
        if (completingScope !== null && !completesScope) throw new TypeError("Invalid DOM update scope.");
        if (!Array.isArray(handles) || !Array.isArray(tags) || !Array.isArray(values)
            || handles.length !== tags.length) throw new TypeError('Invalid DOM batch nodes.');
        if (!(Array.isArray(writes) || writes instanceof Int32Array)
            || !writes.every(Number.isInteger)) throw new TypeError('Invalid DOM batch operations.');
        const nodeIndex = index => {
            if (!Number.isInteger(index) || index < 0 || index >= handles.length) {
                throw new TypeError('Invalid DOM batch node index.');
            }
        };
        const valueIndex = index => {
            if (!Number.isInteger(index) || index < 0 || index >= values.length || typeof values[index] !== 'string') {
                throw new TypeError('Invalid DOM batch string index.');
            }
        };
        for (let index = 0; index < tags.length; index++) {
            if (typeof tags[index] !== 'string' || !tags[index] && !handles[index]?.nodeType) {
                throw new TypeError('Invalid DOM batch node.');
            }
        }
        const offsets = [];
        for (let cursor = 0; cursor < writes.length;) {
            if (cursor + 4 > writes.length) throw new TypeError('Incomplete DOM batch operation.');
            const offset = cursor;
            offsets.push(offset);
            nodeIndex(writes[offset + 1]);
            const first = writes[offset + 2], second = writes[offset + 3];
            cursor += 4;
            switch (writes[offset]) {
                case kind.text: case kind.id: valueIndex(first); break;
                case kind.attribute: case kind.listen: valueIndex(first); valueIndex(second); break;
                case kind.class:
                    valueIndex(first);
                    if (second !== 0 && second !== 1) throw new TypeError('Invalid DOM batch class.');
                    break;
                case kind.runs:
                    if (first < 0 || first > (writes.length - cursor) / 2) throw new TypeError('Invalid DOM batch text runs.');
                    for (let index = 0; index < first * 2; index++) valueIndex(writes[cursor++]);
                    break;
                case kind.editorLines: {
                    if (second < 0 || cursor + 7 > writes.length) throw new TypeError('Invalid editor window.');
                    const roots = [writes[offset + 1], first, ...writes.slice(cursor, cursor + 4)];
                    for (const index of roots) {
                        nodeIndex(index);
                        if (tags[index] === '#text' || !tags[index] && handles[index]?.nodeType !== 1) throw new TypeError('Invalid editor root.');
                    }
                    if (new Set(roots).size !== 6 || (writes[cursor + 4] !== 0 && writes[cursor + 4] !== 1)
                        || writes[cursor + 4] === 1 && (writes[cursor + 5] < -2147483648 || writes[cursor + 5] > 2147483647)
                        || (writes[cursor + 6] !== 0 && writes[cursor + 6] !== 1)) throw new TypeError('Invalid editor window flags.');
                    cursor += 7;
                    if (second > (writes.length - cursor) / 5) throw new TypeError('Incomplete editor window.');
                    const identities = new Set();
                    let previous = -1;
                    for (let row = 0; row < second; row++) {
                        if (cursor + 5 > writes.length) throw new TypeError('Incomplete editor row.');
                        const identity = writes[cursor++], index = writes[cursor++], className = writes[cursor++], text = writes[cursor++], count = writes[cursor++];
                        if (identity < -2147483648 || identity > 2147483647 || index <= previous || index > 2147483647
                            || identities.has(identity) || count < -1) throw new TypeError('Invalid editor row.');
                        identities.add(identity); previous = index;
                        if (className !== -1) valueIndex(className);
                        if (count === -1) valueIndex(text);
                        else {
                            if (text !== -1 || count > (writes.length - cursor) / 2) throw new TypeError('Invalid editor runs.');
                            for (let index = 0; index < count * 2; index++) valueIndex(writes[cursor++]);
                        }
                    }
                    break;
                }
                case kind.activeEditorLine:
                    if ((second !== 0 && second !== 1) || first < -2147483648 || first > 2147483647) throw new TypeError('Invalid active editor row.');
                    break;
                case kind.clearEditorLines: case kind.disposeEditorLines:
                    if (first !== -1 || second !== -1) throw new TypeError('Invalid editor row release.');
                    break;
                case kind.selection:
                    if (cursor + 3 > writes.length || (writes[cursor + 2] !== 0 && writes[cursor + 2] !== 1)) {
                        throw new TypeError('Invalid DOM batch selection.');
                    }
                    cursor += 3;
                    break;
                case kind.insert: nodeIndex(first); if (second !== -1) nodeIndex(second); break;
                case kind.remove: break;
                default: throw new TypeError('Invalid DOM batch operation.');
            }
        }
        const nodes = handles.slice();
        let next = 0;
        for (let index = 0; index < tags.length; index++) {
            if (!tags[index]) continue;
            nodes[index] = tags[index] === '#text' ? document.createTextNode('') : domImports.create(tags[index]);
            created[next++] = nodes[index];
        }
        for (const offset of offsets) {
            const node = nodes[writes[offset + 1]], first = writes[offset + 2], second = writes[offset + 3];
            switch (writes[offset]) {
                case kind.text: domImports.setText(node, values[first]); break;
                case kind.runs: {
                    const text = [], classes = [];
                    for (let index = 0; index < first; index++) {
                        text.push(values[writes[offset + 4 + index * 2]]);
                        classes.push(values[writes[offset + 5 + index * 2]]);
                    }
                    domImports.setTextRuns(node, text, classes);
                    break;
                }
                case kind.editorLines: {
                    let cursor = offset + 11;
                    const rows = [];
                    for (let index = 0; index < second; index++) {
                        const identity = writes[cursor++], lineIndex = writes[cursor++], className = writes[cursor++], text = writes[cursor++], count = writes[cursor++];
                        const runs = count < 0 ? null : [];
                        for (let run = 0; run < count; run++) runs.push({ text: values[writes[cursor++]], className: values[writes[cursor++]] });
                        rows.push({ identity, index: lineIndex, className: className < 0 ? null : values[className], text: text < 0 ? null : values[text], runs });
                    }
                    renderEditorLines(nodes[first], node,
                        nodes[writes[offset + 4]], nodes[writes[offset + 5]], nodes[writes[offset + 6]], nodes[writes[offset + 7]],
                        rows, writes[offset + 8] ? writes[offset + 9] : null, writes[offset + 10] === 1);
                    break;
                }
                case kind.activeEditorLine: setEditorActiveLine(node, second === 1 ? first : null); break;
                case kind.clearEditorLines: clearEditorLines(node); break;
                case kind.disposeEditorLines:
                    try { disposeEditorLines(node); } catch (error) { failure ??= String(error); }
                    break;
                case kind.id: domImports.setId(node, values[first]); break;
                case kind.attribute: domImports.setAttr(node, values[first], values[second]); break;
                case kind.class: domImports.toggleClass(node, values[first], second === 1); break;
                case kind.listen: domImports.listen(node, values[first], values[second]); break;
                case kind.selection:
                    domImports.setEditorSelection(node, first, second, writes[offset + 4], writes[offset + 5], writes[offset + 6] === 1);
                    break;
                case kind.insert: domImports.insertBefore(node, nodes[first], second === -1 ? null : nodes[second]); break;
                case kind.remove: domImports.remove(node); break;
            }
        }
    } catch (error) {
        failure = String(error);
    }
    finally {
        if (completesScope) {
            try { domImports.setAttr(completingScope, "data-editor-updating", "false"); }
            catch (error) { failure ??= String(error); }
        }
    }
    return [...created, failure === null ? null : { error: failure }];
}

const domImports = {
    applyDomBatch,
    find: (id) => document.getElementById(id),
    tryFind: (id) => document.getElementById(id),
    create: (tagName) => tagName === 'svg' || tagName === 'path'
        ? document.createElementNS('http://www.w3.org/2000/svg', tagName)
        : document.createElement(tagName),
    createText: (value) => document.createTextNode(value),
    setText: (element, value) =>
        writeEditorDom(() => {
            element.textContent = value;
        }, element),
    setTextRuns: (element, text, classes) =>
        writeEditorDom(() => {
            const fragment = document.createDocumentFragment();
            for (let index = 0; index < text.length; index++) {
                if (!classes[index]) {
                    fragment.appendChild(document.createTextNode(text[index]));
                } else {
                    const span = document.createElement('span');
                    span.className = classes[index];
                    span.textContent = text[index];
                    fragment.appendChild(span);
                }
            }
            element.replaceChildren(fragment);
        }, element),
    getText: getEditorText,
    getValue: (element) => element.value ?? '',
    setId: (element, id) =>
        writeEditorDom(() => {
            element.id = id;
        }, element),
    setAttr: (element, name, value) => {
        if (name === 'data-editor-updating') {
            setEditorUpdating(element, value === 'true');
        } else {
            writeEditorDom(() => element.setAttribute(name, value), element);
        }
    },
    toggleClass: (element, name, enabled) =>
        writeEditorDom(() => element.classList.toggle(name, enabled), element),
    listen: (element, eventName, payload) => {
        element.addEventListener(eventName, () => emit(payload));
    },
    insertBefore: (parent, child, before) =>
        writeEditorDom(() => parent.insertBefore(child, before), parent, child),
    remove: (element) => writeEditorDom(() => element.remove(), element),
    waitForEvent,
    scheduleEvent,
    cancelScheduledEvent,
    attachEditorSurface: (cellId, surface) =>
        attachEditorSurface(cellId, surface, () => restoreEditorFocus(surface)),
    detachEditorSurface,
    acknowledgeEditorCommand,
    resolveEditorClipboard,
    takeEditorInput,
    attachGutterHeights,
    prepareEditorReconciliation,
    detachGutterHeights,
    setDocumentLines: (lines, totalLines) =>
        writeEditorDom(() => setDocumentLines(lines, totalLines), lines),
    readEditorScrollPosition,
    restoreEditorScroll,
    setEditorSelection: (lines, anchorLine, anchorColumn, headLine, headColumn, reveal) => {
        if (reveal) writeEditorDom(() => cancelEditorScroll(lines), lines);
        setEditorSelection(lines, anchorLine, anchorColumn, headLine, headColumn, reveal);
    },
    placeOverlay,
    clearOverlayPlacement,
    ensureVisible,
    fetchBase64,
    fetchText,
    notebookGetAllJson,
    notebookGetJson,
    notebookPut,
    notebookRemove,
    workspaceGetAllJson,
    workspaceGetJson,
    workspacePut,
    workspaceRemove,
    workspaceRename,
    reportError: (message) => console.error(message),
    createUniformInspector, disposeUniformInspector, createUniformBinding,
    configureUniformBinding, activateUniformBinding, readUniformEdits, disposeUniformBinding,
    readUniformImageEdits, readUniformImagePixels,
    createPipelineCanvas,
    nextPipelineFrame,
    disposePipelineCanvas,
    createShaderPreview,
    setShaderPreviewRevision,
    showShaderPreview,
    disposeShaderPreview,
    downloadShaderBinary,
    appReady: () => dismissBootOverlay(),
};

setModuleImports('main.js', domImports);

async function initializeAssemblyManifest() {
    const config = getConfig();
    const resources = config.resources ?? {};
    const assets = [...(resources.coreAssembly ?? []), ...(resources.assembly ?? [])].filter(
        (asset) => asset.resolvedUrl && asset.virtualPath,
    );
    const virtualPaths = assets.map((asset) => asset.virtualPath);
    const urls = assets.map((asset) => new URL(asset.resolvedUrl, document.baseURI).href);
    const exports = await getAssemblyExports(config.mainAssemblyName);
    await exports.Sia_Examples.Notebook.AssemblyLoader.InitializeAsync(virtualPaths, urls);
}

try {
    await initializeAssemblyManifest();
    await runMain();
} catch (error) {
    console.error(error);
    showBootError('Sia.NET Examples could not start. Reload the page to retry.');
}
