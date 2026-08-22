import { emit } from './notebook-runtime.js';

const gutterHeightmaps = new WeakMap();

function createProbe(lines) {
    const probe = document.createElement('div');
    probe.textContent = 'x'.repeat(32);
    probe.style.position = 'absolute';
    probe.style.visibility = 'hidden';
    probe.style.whiteSpace = 'pre';
    probe.style.pointerEvents = 'none';
    probe.style.left = '-99999px';
    probe.style.top = '-99999px';
    lines.appendChild(probe);
    return probe;
}

function measureAndSync(state) {
    if (!state.probe.isConnected) {
        state.probe = createProbe(state.lines);
    }

    const lineEls = state.lines.querySelectorAll(':scope > .editor-line');
    const gutterEls = state.gutter.querySelectorAll(':scope > .editor-gutter-line');
    const count = Math.min(lineEls.length, gutterEls.length);

    const heights = new Array(count);
    for (let i = 0; i < count; i++) {
        heights[i] = lineEls[i].getBoundingClientRect().height;
    }

    for (let i = 0; i < count; i++) {
        const height = `${heights[i]}px`;
        if (gutterEls[i].style.height !== height) {
            gutterEls[i].style.height = height;
        }
    }

    const probeRect = state.probe.getBoundingClientRect();
    state.lastHeights = heights;
    state.lastFirstLineIndex = count > 0 ? Number(lineEls[0].dataset.ln) : -1;
    state.lastLineHeight = probeRect.height;
    state.lastCharWidth = probeRect.width / state.probe.textContent.length;
    state.lastContentWidth = state.lines.clientWidth;
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
    emit(
        `measure:${state.cellId}:${scrollTop}:${clientHeight}:${firstLineIndex}:` +
            `${state.lastContentWidth}:${state.lastCharWidth}:${state.lastLineHeight}:${heightsCsv}`,
    );
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
        frame: 0,
    };

    const schedule = () => {
        cancelAnimationFrame(state.frame);
        state.frame = requestAnimationFrame(() => {
            state.frame = 0;
            measureAndSync(state);
            reportMeasurement(state);
        });
    };

    const onScroll = () => {
        cancelAnimationFrame(state.frame);
        state.frame = requestAnimationFrame(() => {
            state.frame = 0;
            reportMeasurement(state);
        });
    };

    const resizeObserver = new ResizeObserver(schedule);
    resizeObserver.observe(lines);
    const mutationObserver = new MutationObserver(schedule);
    mutationObserver.observe(lines, { childList: true, subtree: true, characterData: true });
    scrollElement?.addEventListener('scroll', onScroll, { passive: true });

    state.schedule = schedule;
    state.dispose = () => {
        cancelAnimationFrame(state.frame);
        resizeObserver.disconnect();
        mutationObserver.disconnect();
        scrollElement?.removeEventListener('scroll', onScroll);
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

export function scrollLineIntoView(lines, targetTop) {
    const state = gutterHeightmaps.get(lines);
    const scrollElement = lines.parentElement;
    if (!state || !scrollElement) {
        return;
    }

    const viewHeight = scrollElement.clientHeight || 0;
    const currentTop = scrollElement.scrollTop;
    if (targetTop >= currentTop && targetTop <= currentTop + viewHeight) {
        return;
    }

    scrollElement.scrollTop = Math.max(0, targetTop - viewHeight / 2);
    scrollElement.dispatchEvent(new Event('scroll'));
}
