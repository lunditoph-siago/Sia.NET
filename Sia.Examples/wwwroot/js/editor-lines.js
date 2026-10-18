import { writeEditorDom, refreshEditorSelection } from './editor-bridge.js';

const owners = new WeakMap();

function integer(value) {
    return Number.isInteger(value) && value >= -2147483648 && value <= 2147483647;
}

function validateRows(rows, active) {
    if (!Array.isArray(rows) || active !== null && !integer(active)) throw new TypeError('Invalid editor rows.');
    const identities = new Set();
    let previous = -1;
    for (const row of rows) {
        if (!row || !integer(row.identity) || !integer(row.index) || row.index <= previous
            || identities.has(row.identity) || row.className !== null && typeof row.className !== 'string'
            || row.runs === null && typeof row.text !== 'string'
            || row.runs !== null && (!Array.isArray(row.runs)
                || row.runs.some(run => !run || typeof run.text !== 'string' || typeof run.className !== 'string'))) {
            throw new TypeError('Invalid editor row.');
        }
        previous = row.index;
        identities.add(row.identity);
    }
    return identities;
}

function sameRow(a, b) {
    return a && a.className === b.className
        && (a.runs === null ? b.runs === null && a.text === b.text
            : b.runs !== null && a.runs.length === b.runs.length
                && a.runs.every((run, index) => run.text === b.runs[index].text && run.className === b.runs[index].className));
}

function removeRow(owner, identity, row) {
    row.content?.remove();
    owner.rows.delete(identity);
}

function clearRows(owner) {
    const failures = [];
    for (const [identity, row] of owner.rows) {
        try { removeRow(owner, identity, row); } catch (error) { failures.push(error); }
    }
    const remaining = [];
    for (const chunk of owner.chunks) {
        try { chunk.remove(); } catch (error) { remaining.push(chunk); failures.push(error); }
    }
    owner.chunks = remaining;
    owner.pinned.clear();
    if (failures.length) throw new AggregateError(failures, 'Could not clear editor rows.');
}

function renderContent(element, row) {
    if (row.runs === null) {
        element.textContent = row.text;
        return;
    }
    const fragment = document.createDocumentFragment();
    for (const run of row.runs) {
        if (!run.className) fragment.appendChild(document.createTextNode(run.text));
        else {
            const span = document.createElement('span');
            span.className = run.className;
            span.textContent = run.text;
            fragment.appendChild(span);
        }
    }
    element.replaceChildren(fragment);
}

function renderRows(owner, rows, identities, active, force) {
    if (owner.disposed) return;
    const acquired = [];
    try {
        if (owner.dirty) clearRows(owner);
        for (const [identity, row] of owner.rows) {
            if (!identities.has(identity)) removeRow(owner, identity, row);
        }
        const digits = String(rows.at(-1)?.index + 1 || 1).length;
        if (owner.lines.style.getPropertyValue('--editor-line-digits') !== String(digits)) {
            owner.lines.style.setProperty('--editor-line-digits', String(digits));
        }
        const count = Math.ceil(rows.length / 50);
        while (owner.chunks.length > count) {
            owner.chunks.at(-1).remove();
            owner.chunks.pop();
        }
        while (owner.chunks.length < count) {
            const chunk = document.createElement('div');
            chunk.className = 'editor-chunk';
            owner.chunks.push(chunk);
        }
        let nextChunk = owner.linesAfter;
        for (let i = count - 1; i >= 0; i--) {
            const chunk = owner.chunks[i], size = Math.min(50, rows.length - i * 50);
            chunk.style.minBlockSize = size * 1.5 + 'em';
            chunk.style.containIntrinsicBlockSize = 'auto ' + size * 1.5 + 'em';
            if (chunk.parentElement !== owner.lines || chunk.nextSibling !== nextChunk) owner.lines.insertBefore(chunk, nextChunk);
            nextChunk = chunk;
            let next = null;
            for (let n = Math.min(rows.length, (i + 1) * 50) - 1; n >= i * 50; n--) {
                const view = rows[n];
                let row = owner.rows.get(view.identity);
                if (!row) {
                    row = { content: null, view: null };
                    owner.rows.set(view.identity, row);
                    acquired.push([view.identity, row]);
                    row.content = document.createElement('div');
                }
                if (force || row.view?.index !== view.index) {
                    row.content.dataset.ln = String(view.index);
                    row.content.dataset.number = String(view.index + 1);
                }
                if (force || !sameRow(row.view, view)) {
                    row.content.className = view.className === null ? 'editor-line' : 'editor-line ' + view.className;
                    renderContent(row.content, view);
                }
                row.content.classList.toggle('active', active === view.identity);
                if (row.content.parentElement !== chunk || row.content.nextSibling !== next) chunk.insertBefore(row.content, next);
                row.view = view;
                next = row.content;
            }
        }
        owner.dirty = false;
        pinEditorLines(owner.lines);
    } catch (error) {
        owner.dirty = true;
        const failures = [error];
        for (const [identity, row] of acquired) {
            try { removeRow(owner, identity, row); } catch (cleanupError) { failures.push(cleanupError); }
        }
        if (failures.length > 1) throw new AggregateError(failures, 'Editor rows failed and cleanup was incomplete.');
        throw error;
    }
}

export function renderEditorLines(gutter, lines, gutterBefore, gutterAfter, linesBefore, linesAfter, rows, active, force = false) {
    const roots = [gutter, lines, gutterBefore, gutterAfter, linesBefore, linesAfter];
    if (new Set(roots).size !== roots.length || roots.some(node => node?.nodeType !== 1)) throw new TypeError('Invalid editor row roots.');
    const identities = validateRows(rows, active);
    let owner = owners.get(lines);
    if (!owner) {
        owner = { gutter, lines, gutterBefore, gutterAfter, linesBefore, linesAfter, rows: new Map(), chunks: [], pinned: new Set(), disposed: false, dirty: false };
        initializeLayout(owner);
        owners.set(lines, owner);
    } else if (owner.disposed) throw new Error('Editor row owner is disposed.');
    else if (owner.gutter !== gutter || owner.gutterBefore !== gutterBefore || owner.gutterAfter !== gutterAfter
        || owner.linesBefore !== linesBefore || owner.linesAfter !== linesAfter) throw new TypeError('Editor row owner changed roots.');
    writeEditorDom(() => renderRows(owner, rows, identities, active, force), lines);
}

export function setEditorActiveLine(lines, identity) {
    if (identity !== null && !integer(identity)) throw new TypeError('Invalid active editor row.');
    const owner = owners.get(lines);
    if (!owner) return;
    writeEditorDom(() => {
        if (owner.disposed) return;
        for (const [key, row] of owner.rows) row.content?.classList.toggle('active', key === identity);
    }, lines);
}

export function clearEditorLines(lines) {
    const owner = owners.get(lines);
    if (owner) writeEditorDom(() => {
        owner.dirty = true;
        clearRows(owner);
        owner.dirty = false;
    }, lines);
}

export function disposeEditorLines(lines) {
    const owner = owners.get(lines);
    if (!owner) return;
    owner.disposed = true;
    owner.disposeLayout();
    writeEditorDom(() => {
        clearRows(owner);
        if (owners.get(lines) === owner) owners.delete(lines);
    }, lines);
}

function intersectingChunks(lines) {
    const chunks = [...lines.querySelectorAll(':scope > .editor-chunk')];
    const viewport = lines.parentElement.getBoundingClientRect();
    let lower = 0, upper = chunks.length;
    while (lower < upper) {
        const middle = (lower + upper) >>> 1;
        if (chunks[middle].getBoundingClientRect().bottom <= viewport.top) lower = middle + 1;
        else upper = middle;
    }
    const visible = [];
    for (let i = lower; i < chunks.length && chunks[i].getBoundingClientRect().top < viewport.bottom; i++) visible.push(chunks[i]);
    return visible;
}

export function pinEditorLines(lines) {
    const owner = owners.get(lines);
    if (!owner || owner.disposed || !lines.isConnected) return;
    for (let pass = 0; pass < 4; pass++) {
        const next = new Set(intersectingChunks(lines));
        if (next.size === owner.pinned.size && [...next].every(chunk => owner.pinned.has(chunk))) return;
        for (const chunk of owner.pinned) if (!next.has(chunk)) chunk.style.contentVisibility = '';
        for (const chunk of next) if (!owner.pinned.has(chunk)) chunk.style.contentVisibility = 'visible';
        owner.pinned = next;
    }
}

export function visibleEditorLines(lines) {
    pinEditorLines(lines);
    const viewport = lines.parentElement.getBoundingClientRect();
    return intersectingChunks(lines).flatMap(chunk => [...chunk.querySelectorAll('.editor-line')])
        .filter(row => { const rect = row.getBoundingClientRect(); return rect.bottom > viewport.top && rect.top < viewport.bottom; });
}

function initializeLayout(owner) {
    const scroll = owner.lines.parentElement;
    const pin = () => pinEditorLines(owner.lines);
    let frame = 0;
    const refresh = () => {
        pin();
        if (frame) cancelAnimationFrame(frame);
        frame = requestAnimationFrame(() => { frame = 0; refreshEditorSelection(owner.lines); });
    };
    const resize = new ResizeObserver(pin);
    const style = new MutationObserver(pin);
    owner.disposeLayout = () => {
        resize.disconnect();
        style.disconnect();
        scroll.removeEventListener('scroll', refresh);
        if (frame) cancelAnimationFrame(frame);
        owner.pinned.clear();
    };
    try {
        scroll.addEventListener('scroll', refresh, { passive: true });
        resize.observe(scroll);
        style.observe(scroll.closest('.editor-container'), { attributes: true, attributeFilter: ['style'] });
    } catch (error) {
        owner.disposeLayout();
        throw error;
    }
}
