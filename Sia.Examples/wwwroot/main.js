import { dotnet } from './_framework/dotnet.js';
import './js/app-shell.js';
import { bootModuleConfig, dismissBootOverlay, showBootError } from './js/boot-progress.js';
import './js/cell-interactions.js';
import {
    acknowledgeEditorCommand,
    attachEditorSurface,
    detachEditorSurface,
    ignoreOwnWrites,
    setEditorSelection,
} from './js/editor-bridge.js';
import {
    attachGutterHeights,
    detachGutterHeights,
    scrollLineIntoView,
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
} from './js/notebook-runtime.js';

dotnet.withModuleConfig?.(bootModuleConfig);
const { setModuleImports, runMain, getConfig, getAssemblyExports } = await dotnet.create();

setModuleImports('main.js', {
    find: (id) => document.getElementById(id),
    tryFind: (id) => document.getElementById(id),
    create: (tagName) => document.createElement(tagName),
    createText: (value) => document.createTextNode(value),
    setText: (element, value) =>
        ignoreOwnWrites(() => {
            element.textContent = value;
        }, element),
    getText: (element) => element.textContent ?? '',
    getValue: (element) => element.value ?? '',
    setId: (element, id) =>
        ignoreOwnWrites(() => {
            element.id = id;
        }, element),
    setAttr: (element, name, value) =>
        ignoreOwnWrites(() => element.setAttribute(name, value), element),
    toggleClass: (element, name, enabled) =>
        ignoreOwnWrites(() => element.classList.toggle(name, enabled), element),
    listen: (element, eventName, payload) => {
        element.addEventListener(eventName, () => emit(payload));
    },
    insertBefore: (parent, child, before) =>
        ignoreOwnWrites(() => parent.insertBefore(child, before), parent),
    remove: (element) => ignoreOwnWrites(() => element.remove(), element),
    waitForEvent,
    scheduleEvent,
    cancelScheduledEvent,
    attachEditorSurface,
    detachEditorSurface,
    acknowledgeEditorCommand,
    attachGutterHeights,
    detachGutterHeights,
    setDocumentLines,
    scrollLineIntoView,
    setEditorSelection,
    placeOverlay,
    clearOverlayPlacement,
    ensureVisible,
    fetchBase64,
    fetchText,
    notebookGetAllJson,
    notebookGetJson,
    notebookPut,
    notebookRemove,
    reportError: (message) => console.error(message),
    appReady: () => dismissBootOverlay(),
});

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
