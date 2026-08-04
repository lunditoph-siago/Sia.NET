import { dotnet } from './_framework/dotnet.js'

document.getElementById('sidebar-toggle')?.addEventListener('click', () => {
    document.getElementById('app')?.classList.toggle('sidebar-open');
});

const { setModuleImports, runMain, getConfig, getAssemblyExports } = await dotnet.create();

const events = [];
const listeners = [];

function emit(payload) {
    if (listeners.length > 0) {
        listeners.shift()(payload);
    } else {
        events.push(payload);
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
        if (element.textContent !== value) {
            element.textContent = value;
        }
    },
    getValue(element) {
        return element.value ?? "";
    },
    setId(element, id) {
        element.id = id;
    },
    setPosition(element, top, left) {
        element.style.position = 'fixed';
        element.style.top = top + 'px';
        element.style.left = left + 'px';
    },
    toggleClass(element, name, enabled) {
        element.classList.toggle(name, enabled);
    },
    listen(element, name, payload) {
        element.addEventListener(name, () => emit(payload));
    },
    insertBefore(parent, child, before) {
        parent.insertBefore(child, before);
    },
    remove(element) {
        element.remove();
    },
    waitForEvent() {
        if (events.length > 0) {
            return Promise.resolve(events.shift());
        }
        return new Promise(resolve => listeners.push(resolve));
    },
});

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

await initNotebook();
await runMain();
