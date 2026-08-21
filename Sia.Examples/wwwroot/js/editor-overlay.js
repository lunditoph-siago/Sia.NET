const overlayPlacements = new WeakMap();
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
    const viewportRect = viewport.getBoundingClientRect();
    if (
        !anchorRect ||
        anchorRect.bottom < viewportRect.top ||
        anchorRect.top > viewportRect.bottom ||
        anchorRect.right < viewportRect.left ||
        anchorRect.left > viewportRect.right
    ) {
        overlay.style.visibility = 'hidden';
        return;
    }

    const margin = 4;
    const rootFontSize =
        Number.parseFloat(getComputedStyle(document.documentElement).fontSize) || 16;
    const availableWidth = Math.max(0, viewportRect.width - margin * 2);
    if (!availableWidth) {
        overlay.style.visibility = 'hidden';
        return;
    }

    overlay.style.visibility = 'hidden';
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
        };
        placement.schedule = () => {
            cancelAnimationFrame(placement.frame);
            placement.frame = requestAnimationFrame(() => positionOverlay(placement));
        };
        viewport.addEventListener('scroll', placement.schedule, {
            passive: true,
            signal: controller.signal,
        });
        window.addEventListener('resize', placement.schedule, {
            passive: true,
            signal: controller.signal,
        });
        overlayPlacements.set(overlay, placement);
    }

    placement.lineIndex = lineIndex;
    placement.column = column;
    cancelAnimationFrame(placement.frame);
    placement.frame = 0;
    positionOverlay(placement);
}

export function clearOverlayPlacement(overlay) {
    const placement = overlayPlacements.get(overlay);
    if (!placement) {
        return;
    }
    cancelAnimationFrame(placement.frame);
    placement.controller.abort();
    overlayPlacements.delete(overlay);
    delete overlay.dataset.overlaySide;
}

export function ensureVisible(container, element) {
    const top = element.offsetTop;
    const bottom = top + element.offsetHeight;
    if (top < container.scrollTop) {
        container.scrollTop = top;
    } else if (bottom > container.scrollTop + container.clientHeight) {
        container.scrollTop = bottom - container.clientHeight;
    }
}
