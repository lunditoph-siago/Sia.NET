import { emit } from './notebook-runtime.js';
import { getEditorText, getEditorTextPoint } from './editor-bridge.js';
import { visibleEditorLines, pinEditorLines } from './editor-lines.js';

const gutterHeightmaps = new WeakMap();

function verticalScale(state) {
    if (!state) return 1;
    const height = parseFloat(getComputedStyle(state.probe).height);
    return height > 0 ? state.probe.getBoundingClientRect().height / height || 1 : 1;
}

function textRect(line, column, length) {
    if (length === 0) return line.getBoundingClientRect();
    const start = getEditorTextPoint(line, column);
    const end = getEditorTextPoint(line, Math.min(column + 1, length));
    const range = document.createRange();
    range.setStart(start.node, start.offset);
    range.setEnd(end.node, end.offset);
    return range.getClientRects()[0] ?? line.getBoundingClientRect();
}

function currentAnchor(state, line) {
    const anchor = state?.anchor;
    return anchor?.line === line && anchor.index === line.dataset.ln && anchor.text === line.textContent
        && Math.abs(anchor.scrollTop - state.lines.parentElement.scrollTop) <= 0.5 ? anchor : null;
}

function visibleColumn(line, length, top, state) {
    const anchor = currentAnchor(state, line);
    if (anchor) return Math.min(anchor.column, length);
    let from = 0, to = length;
    while (from < to) {
        const middle = (from + to) >>> 1;
        if (textRect(line, middle, length).bottom <= top) from = middle + 1;
        else to = middle;
    }
    return from;
}

export function readEditorScrollPosition(lines) {
    const scroll = lines.parentElement;
    if (!lines.isConnected || !scroll?.clientHeight) {
        return 'null';
    }
    if (lines.dataset.editorUpdating === 'true') {
        return JSON.stringify([scroll.scrollTop, scroll.scrollLeft, -1, 0, 0]);
    }
    const state = gutterHeightmaps.get(lines);
    const scale = verticalScale(state);
    const top = scroll.getBoundingClientRect().top + scroll.clientTop * scale;
    const line = visibleEditorLines(lines)
        .find(row => {
            const rect = row.getBoundingClientRect();
            return rect.bottom > top && rect.top < top + scroll.clientHeight * scale;
        });
    if (!line) {
        return JSON.stringify([scroll.scrollTop, scroll.scrollLeft, -1, 0, 0]);
    }
    const length = getEditorText(line).length;
    const from = visibleColumn(line, length, top, state);
    const height = gutterHeightmaps.get(lines)?.lastLineHeight || line.getBoundingClientRect().height;
    return JSON.stringify([scroll.scrollTop, scroll.scrollLeft, Number(line.dataset.ln), from,
        currentAnchor(state, line)?.offset ?? (textRect(line, from, length).top - top) / (height * scale)]);
}

function applyScrollRestore(state) {
    const pending = state.pendingRestore;
    const scroll = state.lines.parentElement;
    if (!pending || !state.lines.isConnected || !scroll?.clientHeight
        || state.lines.dataset.editorUpdating === 'true') {
        return;
    }
    const line = state.lines.querySelector(`:scope .editor-line[data-ln="${pending.line}"]`);
    if (!line) {
        return;
    }
    const length = getEditorText(line).length;
    const rect = textRect(line, Math.min(pending.column, length), length);
    const scale = verticalScale(state);
    const targetTop = scroll.scrollTop + (rect.top - scroll.getBoundingClientRect().top) / scale - scroll.clientTop
        - pending.offset * state.lastLineHeight;
    scroll.scrollTop = targetTop;
    scroll.scrollLeft = pending.left;
    state.restoreGeneration = pending.generation;
    state.restoreCancelled = false;
    state.pendingRestore = null;
    const applied = Math.abs(scroll.scrollTop - targetTop) * scale <= 1;
    if (!applied) state.anchor = null;
    rememberAnchor(state, applied ? line : null, Math.min(pending.column, length), pending.offset);
}

export function restoreEditorScroll(lines, generation, top, left, line, column, offset) {
    const state = gutterHeightmaps.get(lines);
    if (!state || generation <= state.restoreGeneration || generation < (state.pendingRestore?.generation ?? 0)) {
        return;
    }
    state.pendingRestore = { generation, top, left, line, column, offset };
    state.schedule();
}

export function cancelEditorScroll(lines) {
    const state = gutterHeightmaps.get(lines);
    if (state?.pendingRestore) {
        state.restoreGeneration = state.pendingRestore.generation;
        state.restoreCancelled = true;
        state.pendingRestore = null;
        state.anchor = null;
        state.schedule();
    }
}

export function restoreEditorFocus(lines) {
    const state = gutterHeightmaps.get(lines);
    const scroll = lines.parentElement;
    if (state && lines.isConnected && scroll?.clientHeight) {
        scroll.scrollTop = state.scrollTop;
        scroll.scrollLeft = state.scrollLeft;
    }
}

function createProbe(lines) {
    const probe = document.createElement('div');
    probe.textContent = 'x'.repeat(32);
    probe.style.position = 'absolute';
    probe.style.visibility = 'hidden';
    probe.style.whiteSpace = 'pre';
    probe.style.pointerEvents = 'none';
    probe.style.left = '-99999px';
    probe.style.top = '-99999px';
    probe.setAttribute('aria-hidden', 'true');
    lines.parentElement.appendChild(probe);
    return probe;
}

function measureRows(state) {
    if (state.probe.parentElement !== state.lines.parentElement) {
        state.probe.remove();
        state.lines.parentElement.appendChild(state.probe);
    }

    const lineEls = visibleEditorLines(state.lines);
    const count = lineEls.length;

    const probeRect = state.probe.getBoundingClientRect();
    const probeStyle = getComputedStyle(state.probe);
    const lineHeight = parseFloat(probeStyle.height) || probeRect.height;
    const scale = probeRect.height / lineHeight || 1;
    const heights = new Array(count);
    for (let i = 0; i < count; i++) {
        heights[i] = lineEls[i].getBoundingClientRect().height / scale;
    }

    state.lastHeights = heights;
    state.lastFirstLineIndex = count > 0 ? Number(lineEls[0].dataset.ln) : -1;
    state.lastLineHeight = lineHeight;
    state.lastCharWidth = (parseFloat(probeStyle.width) || probeRect.width) / state.probe.textContent.length;
    const gutterWidth = count > 0 ? parseFloat(getComputedStyle(lineEls[0]).paddingInlineStart) || 0 : 0;
    state.lastContentWidth = state.lines.clientWidth - gutterWidth;
}

function reportMeasurement(state) {
    const scrollElement = state.lines.parentElement;
    if (!scrollElement) {
        return;
    }

    const scrollTop = scrollElement.scrollTop;
    const clientHeight = scrollElement.clientHeight || 0;
    const firstLineIndex = state.lastFirstLineIndex;
    const heightsCsv = state.lastHeights.join(',');
    const revision = state.lines.dataset.editorDocumentRevision ?? '0';
    const scrollGeneration = state.restoreCancelled ? -state.restoreGeneration : state.restoreGeneration;
    rememberAnchor(state);
    emit(
        `measure:${state.cellId}:${scrollTop}:${clientHeight}:${firstLineIndex}:` +
            `${state.lastContentWidth}:${state.lastCharWidth}:${state.lastLineHeight}:${heightsCsv}:` +
            `${revision}:${scrollGeneration}`,
        `measure:${state.cellId}:${revision}:${scrollGeneration}`,
    );
}

function rememberAnchor(state, preferredLine, preferredColumn, preferredOffset) {
    if (state.lines.dataset.editorUpdating === 'true') {
        return;
    }
    const scroll = state.lines.parentElement;
    state.scrollTop = scroll.scrollTop;
    state.scrollLeft = scroll.scrollLeft;
    const scale = verticalScale(state);
    const top = scroll.getBoundingClientRect().top + scroll.clientTop * scale;
    const height = parseFloat(getComputedStyle(state.probe).height);
    for (const line of visibleEditorLines(state.lines)) {
        const rect = line.getBoundingClientRect();
        if (rect.bottom > top && rect.top < top + scroll.clientHeight * scale) {
            const length = getEditorText(line).length;
            const column = line === preferredLine ? preferredColumn : visibleColumn(line, length, top, state);
            const offset = line === preferredLine ? preferredOffset : currentAnchor(state, line)?.offset;
            state.anchor = {
                line, index: line.dataset.ln, text: line.textContent,
                column, length, offset: offset ?? (textRect(line, column, length).top - top) / (height * scale),
                scrollTop: scroll.scrollTop,
            };
            return;
        }
    }
    state.anchor = null;
}

function restoreAnchor(state) {
    const anchor = state.anchor;
    if (state.pendingRestore || !state.lines.isConnected || !state.lines.parentElement?.clientHeight
        || !anchor || !state.lines.contains(anchor.line)
        || anchor.line.dataset.ln !== anchor.index || anchor.line.textContent !== anchor.text) {
        return;
    }
    const scroll = state.lines.parentElement;
    const unchangedScroll = Math.abs(scroll.scrollTop - anchor.scrollTop) <= 0.5;
    const height = parseFloat(getComputedStyle(state.probe).height);
    const offset = (textRect(anchor.line, anchor.column, anchor.length).top - scroll.getBoundingClientRect().top) / verticalScale(state)
        - scroll.clientTop - anchor.offset * height
        + scroll.scrollTop - anchor.scrollTop;
    const targetTop = scroll.scrollTop + offset;
    if (Math.abs(offset) > 0.5) {
        scroll.scrollTop = targetTop;
    }
    const applied = unchangedScroll && Math.abs(scroll.scrollTop - targetTop) * verticalScale(state) <= 1;
    if (!applied) {
        state.anchor = null;
    }
    rememberAnchor(state, applied ? anchor.line : null, anchor.column, anchor.offset);
}

export function attachGutterHeights(gutter, lines, cellId) {
    if (gutterHeightmaps.has(lines)) {
        return;
    }

    const scrollElement = lines.parentElement;
    const state = {
        gutter,
        lines,
        cellId,
        probe: createProbe(lines),
        documentLines: 0,
        lastHeights: [],
        lastFirstLineIndex: -1,
        lastLineHeight: 0,
        lastCharWidth: 0,
        lastContentWidth: 0,
        report: null,
        needsMeasurement: true,
        anchor: null,
        restoreGeneration: 0,
        restoreCancelled: false,
        pendingRestore: null,
        scrollTop: scrollElement.scrollTop,
        scrollLeft: scrollElement.scrollLeft,
    };

    const isCurrent = () => gutterHeightmaps.get(lines) === state;
    const onScroll = () => {
        if (!isCurrent()) return;
        pinEditorLines(lines);
        state.scrollTop = scrollElement.scrollTop;
        state.scrollLeft = scrollElement.scrollLeft;
        if (state.report) {
            return;
        }
        const report = { frame: 0, timer: 0 };
        state.report = report;
        const run = () => {
            if (!isCurrent() || state.report !== report) return;
            state.report = null;
            cancelAnimationFrame(report.frame);
            clearTimeout(report.timer);
            if (!lines.isConnected || !lines.parentElement?.clientHeight || lines.dataset.editorUpdating === 'true') {
                state.needsMeasurement = true;
                return;
            }
            restoreAnchor(state);
            if (state.needsMeasurement) {
                state.needsMeasurement = false;
                measureRows(state);
            }
            applyScrollRestore(state);
            reportMeasurement(state);
        };
        report.frame = requestAnimationFrame(run);
        report.timer = setTimeout(run, 50);
    };

    const schedule = () => {
        if (!isCurrent()) return;
        state.needsMeasurement = true;
        if (lines.isConnected && lines.parentElement?.clientHeight) {
            measureRows(state);
            state.needsMeasurement = false;
        }
        onScroll();
    };

    const resizeObserver = new ResizeObserver(() => {
        if (!isCurrent()) return;
        restoreAnchor(state);
        schedule();
    });
    resizeObserver.observe(lines);
    resizeObserver.observe(state.probe);
    const mutationObserver = new MutationObserver((records) => {
        if (!isCurrent()) return;
        restoreAnchor(state);
        if (records.some((record) => record.type !== 'attributes'
            || record.attributeName === 'style' || record.attributeName === 'data-ln')) {
            state.needsMeasurement = true;
        }
        if (state.needsMeasurement) {
            schedule();
        } else if (records.some(record => record.attributeName === 'data-editor-document-revision')) {
            onScroll();
        }
    });
    mutationObserver.observe(lines, {
        childList: true, subtree: true, characterData: true,
        attributes: true, attributeFilter: ['data-editor-updating', 'data-editor-document-revision', 'style', 'data-ln'],
    });
    scrollElement?.addEventListener('scroll', onScroll, { passive: true });
    const onUserScroll = () => cancelEditorScroll(lines);
    scrollElement?.addEventListener('wheel', onUserScroll, { passive: true });
    scrollElement?.addEventListener('pointerdown', onUserScroll, { passive: true });

    state.schedule = schedule;
    state.dispose = () => {
        const report = state.report;
        state.report = null;
        if (report) {
            cancelAnimationFrame(report.frame);
            clearTimeout(report.timer);
        }
        resizeObserver.disconnect();
        mutationObserver.disconnect();
        scrollElement?.removeEventListener('scroll', onScroll);
        scrollElement?.removeEventListener('wheel', onUserScroll);
        scrollElement?.removeEventListener('pointerdown', onUserScroll);
        state.probe.remove();
    };

    gutterHeightmaps.set(lines, state);
    schedule();
}

export function detachGutterHeights(lines) {
    const state = gutterHeightmaps.get(lines);
    if (!state) {
        return;
    }
    state.dispose();
    gutterHeightmaps.delete(lines);
}

export function setDocumentLines(lines, totalLines) {
    const state = gutterHeightmaps.get(lines);
    if (!state || state.documentLines === totalLines) {
        return;
    }
    state.documentLines = totalLines;
    state.schedule();
}
