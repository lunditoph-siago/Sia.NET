// Each store owns its cached connection; callers own the transaction's mutations.
function createStore(databaseName, storeName, keyPath) {
    let connection;
    function open() {
        connection ??= new Promise((resolve, reject) => {
            const request = indexedDB.open(databaseName, 1);
            request.onupgradeneeded = () => {
                if (!request.result.objectStoreNames.contains(storeName)) {
                    request.result.createObjectStore(storeName, { keyPath });
                }
            };
            request.onsuccess = () => {
                const db = request.result;
                db.onversionchange = () => { db.close(); connection = undefined; };
                resolve(db);
            };
            request.onerror = () => { connection = undefined; reject(request.error); };
        });
        return connection;
    }
    return async (mode, operation) => {
        const db = await open();
        return new Promise((resolve, reject) => {
            const tx = db.transaction(storeName, mode);
            let result;
            tx.oncomplete = () => resolve(result);
            tx.onerror = () => reject(tx.error);
            tx.onabort = () => reject(tx.error ?? new Error('IndexedDB transaction aborted'));
            try {
                const request = operation(tx.objectStore(storeName));
                if (request) request.addEventListener('success', () => { result = request.result; });
            } catch (error) {
                tx.abort();
                reject(error);
            }
        });
    };
}

const withNotebookStore = createStore('sia-notebooks', 'notebooks', 'key');
const withWorkspaceStore = createStore('sia-editor', 'workspace', 'path');

// Compare the record read by C# inside the same transaction that writes it.
function matches(current, expected, compare) {
    return !compare || (expected === null
        ? current === undefined
        : JSON.stringify(current) === expected);
}

async function putRecord(withStore, key, record, expected, compare) {
    let written = false;
    await withStore('readwrite', (store) => {
        const request = store.get(key);
        request.addEventListener('success', () => {
            if (matches(request.result, expected, compare)) {
                store.put(record);
                written = true;
            }
        });
        return request;
    });
    return written;
}

export async function notebookGetAllJson() {
    const records = await withNotebookStore('readonly', (store) => store.getAll());
    return JSON.stringify(records ?? []);
}

export async function notebookGetJson(key) {
    const record = await withNotebookStore('readonly', (store) => store.get(key));
    return record ? JSON.stringify(record) : null;
}

export function notebookPut(key, xml, savedAt, expected = null, compare = false) {
    return putRecord(withNotebookStore, key, { key, xml, savedAt }, expected, compare);
}

export async function notebookRemove(key, expected = null, compare = false) {
    let removed = false;
    let current;
    await withNotebookStore('readwrite', (store) => {
        const request = store.get(key);
        request.addEventListener('success', () => {
            const record = request.result;
            if (!matches(record, expected, compare)) {
                current = record;
            } else if (record !== undefined) {
                store.delete(key);
                removed = true;
            }
        });
        return request;
    });
    return JSON.stringify({ removed, current });
}

export async function notebookRename(oldKey, newKey) {
    if (oldKey === newKey) return 0;
    let result = 1;
    await withNotebookStore('readwrite', (store) => {
        const source = store.get(oldKey);
        source.addEventListener('success', () => {
            if (source.result === undefined) return;
            const destination = store.get(newKey);
            destination.addEventListener('success', () => {
                if (destination.result !== undefined) { result = 2; return; }
                store.put({ ...source.result, key: newKey });
                store.delete(oldKey);
                result = 0;
            });
        });
        return source;
    });
    return result;
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
    return await putRecord(withWorkspaceStore, path, record, expected, compare)
        ? JSON.stringify(record) : null;
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
