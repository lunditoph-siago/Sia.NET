const events = [];
const waiters = [];
const resourceRequests = new Map();
const scheduledEvents = new Map();

export function emit(payload) {
    const waiter = waiters.shift();
    if (waiter) {
        waiter(payload);
    } else {
        events.push(payload);
    }
}

export function waitForEvent() {
    const event = events.shift();
    return event === undefined
        ? new Promise((resolve) => waiters.push(resolve))
        : Promise.resolve(event);
}

export function scheduleEvent(key, payload, delayMilliseconds) {
    cancelScheduledEvent(key);
    scheduledEvents.set(
        key,
        setTimeout(() => {
            scheduledEvents.delete(key);
            emit(payload);
        }, delayMilliseconds),
    );
}

export function cancelScheduledEvent(key) {
    const timer = scheduledEvents.get(key);
    if (timer !== undefined) {
        clearTimeout(timer);
        scheduledEvents.delete(key);
    }
}

function loadBytes(url) {
    let request = resourceRequests.get(url);
    if (!request) {
        request = fetch(url).then((response) => {
            if (!response.ok) {
                throw new Error(`HTTP ${response.status} while fetching ${url}`);
            }
            return response.arrayBuffer();
        });
        resourceRequests.set(url, request);
    }
    return request;
}

export async function fetchBase64(url) {
    const bytes = new Uint8Array(await loadBytes(url));
    let binary = '';
    for (let offset = 0; offset < bytes.length; offset += 8192) {
        binary += String.fromCharCode(...bytes.subarray(offset, offset + 8192));
    }
    return btoa(binary);
}

export async function fetchText(url) {
    return new TextDecoder().decode(await loadBytes(url));
}

const NOTEBOOK_DB_NAME = 'sia-notebooks';
const NOTEBOOK_DB_VERSION = 1;
const NOTEBOOK_STORE = 'notebooks';

let notebookDbPromise;

function openNotebookDb() {
    notebookDbPromise ??= new Promise((resolve, reject) => {
        const request = indexedDB.open(NOTEBOOK_DB_NAME, NOTEBOOK_DB_VERSION);
        request.onupgradeneeded = () => {
            if (!request.result.objectStoreNames.contains(NOTEBOOK_STORE)) {
                request.result.createObjectStore(NOTEBOOK_STORE, { keyPath: 'key' });
            }
        };
        request.onsuccess = () => resolve(request.result);
        request.onerror = () => reject(request.error);
    });
    return notebookDbPromise;
}

function withNotebookStore(mode, op) {
    return openNotebookDb().then(
        (db) =>
            new Promise((resolve, reject) => {
                const tx = db.transaction(NOTEBOOK_STORE, mode);
                const store = tx.objectStore(NOTEBOOK_STORE);
                let result;
                const request = op(store);
                if (request) {
                    request.onsuccess = () => {
                        result = request.result;
                    };
                    request.onerror = () => reject(request.error);
                }
                tx.oncomplete = () => resolve(result);
                tx.onerror = () => reject(tx.error);
                tx.onabort = () => reject(tx.error ?? new Error('IndexedDB transaction aborted'));
            }),
    );
}

export async function notebookGetAllJson() {
    const records = await withNotebookStore('readonly', (store) => store.getAll());
    return JSON.stringify(records ?? []);
}

export async function notebookGetJson(key) {
    const record = await withNotebookStore('readonly', (store) => store.get(key));
    return record ? JSON.stringify(record) : null;
}

export async function notebookPut(key, xml, savedAt) {
    await withNotebookStore('readwrite', (store) => store.put({ key, xml, savedAt }));
}

export async function notebookRemove(key) {
    await withNotebookStore('readwrite', (store) => store.delete(key));
}

const WORKSPACE_DB_NAME = 'sia-editor';
const WORKSPACE_DB_VERSION = 1;
const WORKSPACE_STORE = 'workspace';

let workspaceDbPromise;

function openWorkspaceDb() {
    workspaceDbPromise ??= new Promise((resolve, reject) => {
        const request = indexedDB.open(WORKSPACE_DB_NAME, WORKSPACE_DB_VERSION);
        request.onupgradeneeded = () => {
            if (!request.result.objectStoreNames.contains(WORKSPACE_STORE)) {
                request.result.createObjectStore(WORKSPACE_STORE, { keyPath: 'path' });
            }
        };
        request.onsuccess = () => resolve(request.result);
        request.onerror = () => reject(request.error);
    });
    return workspaceDbPromise;
}

function withWorkspaceStore(mode, op) {
    return openWorkspaceDb().then(
        (db) =>
            new Promise((resolve, reject) => {
                const tx = db.transaction(WORKSPACE_STORE, mode);
                const store = tx.objectStore(WORKSPACE_STORE);
                let result;
                const request = op(store);
                if (request) {
                    request.onsuccess = () => {
                        result = request.result;
                    };
                    request.onerror = () => reject(request.error);
                }
                tx.oncomplete = () => resolve(result);
                tx.onerror = () => reject(tx.error);
                tx.onabort = () => reject(tx.error ?? new Error('IndexedDB transaction aborted'));
            }),
    );
}

export async function workspaceGetAllJson() {
    const records = await withWorkspaceStore('readonly', (store) => store.getAll());
    return JSON.stringify(records ?? []);
}

export async function workspaceGetJson(path) {
    const record = await withWorkspaceStore('readonly', (store) => store.get(path));
    return record ? JSON.stringify(record) : null;
}

export async function workspacePut(path, kind, content, modifiedAt) {
    await withWorkspaceStore('readwrite', (store) => store.put({ path, kind, content, modifiedAt }));
}

export async function workspaceRemove(path) {
    await withWorkspaceStore('readwrite', (store) => store.delete(path));
}
