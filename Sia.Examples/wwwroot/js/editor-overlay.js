import { emit } from './notebook-runtime.js';

const overlayPlacements = new WeakMap();
const completionWindows = new WeakMap();
const caretMarkerSelector = '[data-editor-caret-marker]';

function editorPositionRect(surface, lineIndex, column) {
    const line = surface.querySelector(`[data-ln="${lineIndex}"]`);
    if (!line || column < 0) {
        return null;
    }

    let remaining = column;
    let point = null;
    let lastText = null;
    const walker = document.createTreeWalker(line, NodeFilter.SHOW_TEXT);
    for (let node = walker.nextNode(); node; node = walker.nextNode()) {
        if (node.parentElement?.closest?.(caretMarkerSelector)) {
            continue;
        }
        const length = node.nodeValue.length;
        if (remaining <= length) {
            point = { node, offset: remaining };
            break;
        }
        remaining -= length;
        lastText = node;
    }
    if (!point && remaining === 0 && lastText) {
        point = { node: lastText, offset: lastText.nodeValue.length };
    }
    if (!point && column !== 0) {
        return null;
    }
    point ??= { node: line, offset: 0 };

    const range = document.createRange();
    range.setStart(point.node, point.offset);
    range.collapse(true);
    let rect = range.getBoundingClientRect();
    if (rect.height) {
        return rect;
    }

    if (point.node.nodeType === Node.TEXT_NODE && point.node.nodeValue.length) {
        if (point.offset < point.node.nodeValue.length) {
            range.setEnd(point.node, point.offset + 1);
            rect = range.getBoundingClientRect();
            if (rect.height) {
                return {
                    left: rect.left,
                    right: rect.left,
                    top: rect.top,
                    bottom: rect.bottom,
                    width: 0,
                    height: rect.height,
                };
            }
        } else if (point.offset > 0) {
            range.setStart(point.node, point.offset - 1);
            rect = range.getBoundingClientRect();
            if (rect.height) {
                return {
                    left: rect.right,
                    right: rect.right,
                    top: rect.top,
                    bottom: rect.bottom,
                    width: 0,
                    height: rect.height,
                };
            }
        }
    }

    const lineRect = line.getBoundingClientRect();
    return {
        left: lineRect.left,
        right: lineRect.left,
        top: lineRect.top,
        bottom: lineRect.bottom,
        width: 0,
        height: lineRect.height,
    };
}

function positionOverlay(placement) {
    const { container, surface, overlay, viewport, lineIndex, column } = placement;
    if (!overlay.isConnected) {
        clearOverlayPlacement(overlay);
        return;
    }

    const anchorRect = editorPositionRect(surface, lineIndex, column);
    const editorRect = viewport.getBoundingClientRect();
    const visual = window.visualViewport;
    const viewportRect = visual ? {
        left: Math.max(editorRect.left, visual.offsetLeft),
        top: Math.max(editorRect.top, visual.offsetTop),
        right: Math.min(editorRect.right, visual.offsetLeft + visual.width),
        bottom: Math.min(editorRect.bottom, visual.offsetTop + visual.height),
    } : { left: editorRect.left, top: editorRect.top, right: editorRect.right, bottom: editorRect.bottom };
    for (let parent = viewport.parentElement; parent; parent = parent.parentElement) {
        const style = getComputedStyle(parent);
        const clipX = /auto|scroll|hidden|clip/.test(style.overflowX);
        const clipY = /auto|scroll|hidden|clip/.test(style.overflowY);
        if (!clipX && !clipY) continue;
        const clip = parent.getBoundingClientRect();
        if (clipX) {
            viewportRect.left = Math.max(viewportRect.left, clip.left);
            viewportRect.right = Math.min(viewportRect.right, clip.right);
        }
        if (clipY) {
            viewportRect.top = Math.max(viewportRect.top, clip.top);
            viewportRect.bottom = Math.min(viewportRect.bottom, clip.bottom);
        }
    }
    if (
        !anchorRect ||
        viewportRect.right <= viewportRect.left ||
        viewportRect.bottom <= viewportRect.top ||
        anchorRect.bottom <= viewportRect.top ||
        anchorRect.top >= viewportRect.bottom ||
        anchorRect.right < viewportRect.left ||
        anchorRect.left > viewportRect.right
    ) {
        overlay.style.visibility = 'hidden';
        return;
    }

    const margin = 4;
    const rootFontSize =
        Number.parseFloat(getComputedStyle(document.documentElement).fontSize) || 16;
    const availableWidth = Math.max(0, viewportRect.right - viewportRect.left - margin * 2);
    if (!availableWidth) {
        overlay.style.visibility = 'hidden';
        return;
    }

    overlay.style.visibility = 'hidden';
    overlay.dataset.overlayCompact = 'false';
    overlay.style.width = `${Math.min(rootFontSize * 22, availableWidth)}px`;
    overlay.style.height = '';
    overlay.style.maxHeight = '12rem';

    const spaceAbove = Math.max(0, anchorRect.top - viewportRect.top - margin);
    const spaceBelow = Math.max(0, viewportRect.bottom - anchorRect.bottom - margin);
    const naturalHeight = overlay.offsetHeight;
    let side = overlay.dataset.overlaySide || 'below';
    const sideSpace = side === 'above' ? spaceAbove : spaceBelow;
    const otherSpace = side === 'above' ? spaceBelow : spaceAbove;
    if (sideSpace < naturalHeight && otherSpace > sideSpace) {
        side = side === 'above' ? 'below' : 'above';
    }
    overlay.dataset.overlaySide = side;

    const availableHeight = side === 'above' ? spaceAbove : spaceBelow;
    const minimumHeight = overlay.querySelector('.completion-item')?.getBoundingClientRect().height ?? 0;
    if (availableHeight < minimumHeight) return;
    overlay.dataset.overlayCompact = availableHeight < rootFontSize * 8 ? 'true' : 'false';
    overlay.style.maxHeight = `${Math.min(rootFontSize * 12, availableHeight)}px`;
    const overlayRect = overlay.getBoundingClientRect();
    const containerRect = container.getBoundingClientRect();
    const left = Math.max(
        viewportRect.left + margin,
        Math.min(anchorRect.left, viewportRect.right - overlayRect.width - margin),
    );
    const top =
        side === 'above'
            ? anchorRect.top - overlayRect.height - margin
            : anchorRect.bottom + margin;
    overlay.style.left = `${left - containerRect.left}px`;
    overlay.style.top = `${top - containerRect.top}px`;
    overlay.style.visibility = '';
    const list = overlay.querySelector('.completion-list');
    if (list) {
        const selected = list.querySelector('.selected, .soft-selected');
        if (selected) ensureVisible(list, selected);
        scheduleCompletionWindow(list);
    }
}

export function scheduleCompletionWindow(list) {
    let pending = completionWindows.get(list);
    if (!pending) { pending = { frame: 0, sent: '' }; completionWindows.set(list, pending); }
    if (pending.frame) return;
    pending.frame = requestAnimationFrame(() => {
        pending.frame = 0;
        if (!list.isConnected) return;
        const rows = list.querySelectorAll('[data-completion-index]');
        const height = rows[0]?.getBoundingClientRect().height;
        if (!height || !list.clientHeight) return;
        const measured = `${height}px`;
        if (list.style.getPropertyValue('--completion-row-height') !== measured)
            list.style.setProperty('--completion-row-height', measured);
        const start = Number(list.dataset.completionStart);
        const total = Number(list.dataset.completionTotal);
        if (!Number.isFinite(start) || !Number.isFinite(total)) return;
        const first = Math.floor(list.scrollTop / height);
        const last = Math.min(total - 1, Math.floor((list.scrollTop + list.clientHeight - 1) / height));
        const next = first >= start && last < start + rows.length ? start
            : Math.max(0, Math.min(total - rows.length, first - 3));
        const pageSize = Math.max(1, Math.floor(list.clientHeight / height));
        const key = `${list.dataset.completionGeneration}:${start}:${next}:${pageSize}`;
        if (key === pending.sent) return;
        pending.sent = key;
        emit(`suggestion-window:${list.dataset.completionBinding}:${list.dataset.completionGeneration}:${next}:${pageSize}`);
    });
}

export function placeOverlay(container, surface, overlay, lineIndex, column) {
    let placement = overlayPlacements.get(overlay);
    const viewport = surface.parentElement || container;
    if (
        placement &&
        (placement.container !== container ||
            placement.surface !== surface ||
            placement.viewport !== viewport)
    ) {
        clearOverlayPlacement(overlay);
        placement = null;
    }
    if (!placement) {
        const controller = new AbortController();
        placement = {
            container,
            surface,
            overlay,
            viewport,
            lineIndex,
            column,
            controller,
            frame: 0,
            schedule: null,
            resizeObserver: null,
        };
        placement.schedule = () => {
            cancelAnimationFrame(placement.frame);
            placement.frame = requestAnimationFrame(() => positionOverlay(placement));
        };
        placement.resizeObserver = new ResizeObserver(placement.schedule);
        placement.resizeObserver.observe(viewport);
        viewport.addEventListener('scroll', placement.schedule, {
            passive: true,
            signal: controller.signal,
        });
        window.addEventListener('resize', placement.schedule, {
            passive: true,
            signal: controller.signal,
        });
        window.addEventListener('scroll', event => {
            if (event.target === window || event.target?.contains?.(surface)) placement.schedule();
        }, {
            capture: true,
            passive: true,
            signal: controller.signal,
        });
        for (const event of ['resize', 'scroll']) {
            window.visualViewport?.addEventListener(event, placement.schedule, {
                passive: true,
                signal: controller.signal,
            });
        }
        overlayPlacements.set(overlay, placement);
    }

    placement.lineIndex = lineIndex;
    placement.column = column;
    cancelAnimationFrame(placement.frame);
    placement.frame = 0;
    positionOverlay(placement);
}

export function clearOverlayPlacement(overlay) {
    const list = overlay.querySelector('.completion-list');
    if (list) {
        const pending = completionWindows.get(list);
        if (pending) cancelAnimationFrame(pending.frame);
        completionWindows.delete(list);
    }
    const placement = overlayPlacements.get(overlay);
    if (!placement) {
        return;
    }
    cancelAnimationFrame(placement.frame);
    placement.resizeObserver.disconnect();
    placement.controller.abort();
    overlayPlacements.delete(overlay);
    delete overlay.dataset.overlaySide;
    delete overlay.dataset.overlayCompact;
}

export function ensureVisible(container, element) {
    if (container.dataset?.completionReveal === 'false') return;
    const bounds = element.getBoundingClientRect();
    const origin = container.getBoundingClientRect();
    const top = bounds.top - origin.top + container.scrollTop;
    const bottom = bounds.bottom - origin.top + container.scrollTop;
    if (top < container.scrollTop) {
        container.scrollTop = top;
    } else if (bottom > container.scrollTop + container.clientHeight) {
        container.scrollTop = bottom - container.clientHeight;
    }
    const left = bounds.left - origin.left + container.scrollLeft;
    const right = bounds.right - origin.left + container.scrollLeft;
    if (left < container.scrollLeft || bounds.width > container.clientWidth) {
        container.scrollLeft = left;
    } else if (right > container.scrollLeft + container.clientWidth) {
        container.scrollLeft = right - container.clientWidth;
    }
}
