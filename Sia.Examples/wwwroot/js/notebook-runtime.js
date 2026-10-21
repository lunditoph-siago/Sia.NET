const events = [];
const waiters = [];
const barriers = new Map();
let activeBarrier;
const resourceRequests = new Map();
const scheduledEvents = new Map();

export function emit(payload, coalesceKey) {
    const previous = events.at(-1);
    if (coalesceKey !== undefined && previous?.coalesceKey === coalesceKey) {
        previous.payload = payload;
    } else {
        events.push({ payload, coalesceKey });
    }
    flushEvents();
}

export function emitBarrier(payload, key) {
    if (barriers.has(key)) throw new Error('Duplicate event barrier.');
    barriers.set(key, { released: false });
    events.push({ payload, barrier: key });
    flushEvents();
}

export function resolveBarrier(key, followup) {
    const barrier = barriers.get(key);
    if (!barrier || barrier.released) return;
    barrier.released = true;
    barrier.followup = followup;
    if (activeBarrier === key) {
        activeBarrier = undefined;
        barriers.delete(key);
        events.unshift({ payload: followup });
        flushEvents();
    }
}

function takeEvent() {
    if (activeBarrier || !events.length) return undefined;
    const event = events.shift();
    if (event.barrier) {
        const barrier = barriers.get(event.barrier);
        if (barrier.released) {
            barriers.delete(event.barrier);
            events.unshift({ payload: barrier.followup });
        } else activeBarrier = event.barrier;
    }
    return event.payload;
}

function flushEvents() {
    while (waiters.length) {
        const payload = takeEvent();
        if (payload === undefined) break;
        waiters.shift()(payload);
    }
}

export function waitForEvent() {
    const event = takeEvent();
    return event === undefined
        ? new Promise((resolve) => waiters.push(resolve))
        : Promise.resolve(event);
}

export function takeEventBatch(prefix) {
    const batch = [];
    while (!activeBarrier && events[0]?.payload.startsWith(prefix)) {
        batch.push(takeEvent());
    }
    return batch;
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
        }).catch((error) => {
            if (resourceRequests.get(url) === request) resourceRequests.delete(url);
            throw error;
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

export async function workspacePut(path, kind, content, modifiedAt, expected = null, compare = false) {
    const record = { path, kind, content, modifiedAt, version: crypto.randomUUID() };
    let written = false;
    await withWorkspaceStore('readwrite', (store) => {
        const request = store.get(path);
        request.addEventListener('success', () => {
            const current = request.result;
            if (!compare || (expected === null
                ? current === undefined
                : JSON.stringify(current) === expected)) {
                store.put(record);
                written = true;
            }
        });
        return request;
    });
    return written ? JSON.stringify(record) : null;
}

export async function workspaceRemove(path, expected = null, compare = false) {
    let removed = false;
    let current;
    await withWorkspaceStore('readwrite', (store) => {
        const request = store.getAll();
        request.addEventListener('success', () => {
            const records = request.result;
            const target = records.find((record) => record.path === path);
            if (compare && JSON.stringify(target) !== expected) {
                current = target;
                return;
            }
            for (const record of records) {
                if (record.path === path || record.path.startsWith(path + '/')) {
                    store.delete(record.path);
                    removed = true;
                }
            }
        });
        return request;
    });
    return JSON.stringify({ removed, current });
}

export async function workspaceRename(oldPath, newPath) {
    if (oldPath === newPath) return 0;
    let result = 1;
    await withWorkspaceStore('readwrite', (store) => {
        const request = store.getAll();
        request.addEventListener('success', () => {
            const records = request.result;
            const moved = records.filter((record) =>
                record.path === oldPath || record.path.startsWith(oldPath + '/'));
            if (moved.length === 0) return;
            if (records.some((record) =>
                record.path === newPath || record.path.startsWith(newPath + '/'))) {
                result = 2;
                return;
            }
            for (const record of moved) {
                store.put({ ...record, path: newPath + record.path.slice(oldPath.length) });
                store.delete(record.path);
            }
            result = 0;
        });
        return request;
    });
    return result;
}
