import { menuOwner } from './app-shell.js';
import { emit } from './notebook-runtime.js';

const cellDragThreshold = 6;
let cellDrag = null;
let cellPreview = null;
let suppressCellClick = false;
let pendingCellRevision = null;
let splitResize = null;
let pendingCellAnchor = null;

function rememberCellAnchor(target) {
    const owner = menuOwner(target);
    const cell = target?.closest?.('[data-cell-region]') ?? owner?.closest?.('[data-cell-region]');
    const scroller = document.getElementById('notebook');
    if (!cell || !scroller) {
        return;
    }
    pendingCellAnchor = {
        cell,
        top: cell.getBoundingClientRect().top,
    };
}

function restoreCellAnchor() {
    const anchor = pendingCellAnchor;
    pendingCellAnchor = null;
    const scroller = document.getElementById('notebook');
    if (!anchor?.cell.isConnected || !scroller) {
        return;
    }
    scroller.scrollTop += anchor.cell.getBoundingClientRect().top - anchor.top;
}

new MutationObserver((records) => {
    if (
        !pendingCellAnchor ||
        !records.some((record) => record.target.classList?.contains('floating-layer'))
    ) {
        return;
    }
    requestAnimationFrame(restoreCellAnchor);
}).observe(document.getElementById('notebook'), {
    attributes: true,
    attributeFilter: ['data-cell-layout-revision'],
    subtree: true,
});

document.addEventListener(
    'pointerdown',
    (event) => {
        if (
            event.target.closest?.(
                '[data-cell-tab], .cell-split > .separator, .menu-item, .close, ' +
                    '[data-inline-save], [data-inline-discard]',
            )
        ) {
            rememberCellAnchor(event.target);
        }
    },
    true,
);

function currentCellRevision() {
    const value = document.querySelector('.floating-layer')?.dataset.cellLayoutRevision;
    return Number.parseInt(value ?? '0', 10) || 0;
}

function cellLayoutReady() {
    if (pendingCellRevision === null) {
        return true;
    }
    if (currentCellRevision() < pendingCellRevision) {
        return false;
    }
    pendingCellRevision = null;
    return true;
}

function splitRatioAt(split, pointerX, pointerY) {
    const rect = split.getBoundingClientRect();
    const horizontal = split.classList.contains('horizontal');
    const position = horizontal ? pointerX - rect.left : pointerY - rect.top;
    const size = horizontal ? rect.width : rect.height;
    return Math.min(0.85, Math.max(0.15, position / Math.max(size, 1)));
}

function applySplitRatio(split, ratio) {
    const children = split.querySelectorAll(':scope > .split-child');
    children[0]?.style.setProperty('--cell-split-share', ratio.toFixed(4));
    children[1]?.style.setProperty('--cell-split-share', (1 - ratio).toFixed(4));
    split
        .querySelector(':scope > .separator')
        ?.setAttribute('aria-valuenow', `${Math.round(ratio * 100)}`);
}

function finishSplitResize(commit) {
    if (!splitResize) {
        return;
    }
    const resize = splitResize;
    splitResize = null;
    document.body.classList.remove('split-resizing');
    try {
        resize.separator.releasePointerCapture(resize.pointerId);
    } catch {}
    if (!commit) {
        applySplitRatio(resize.split, resize.originalRatio);
        return;
    }
    const ratio = splitRatioAt(resize.split, resize.pointerX, resize.pointerY);
    pendingCellRevision = resize.revision + 1;
    emit(`cell-resize:${resize.revision}:${resize.split.dataset.cellSplit}:${ratio.toFixed(4)}`);
}

document.addEventListener('pointerdown', (event) => {
    const separator = event.target.closest?.('.cell-split > .separator');
    const split = separator?.closest('.cell-split');
    if (
        !separator ||
        !split ||
        separator.getAttribute('aria-disabled') === 'true' ||
        matchMedia('(max-width: 720px)').matches ||
        !cellLayoutReady() ||
        (event.pointerType === 'mouse' && event.button !== 0)
    ) {
        return;
    }
    event.preventDefault();
    finishCellDrag();
    const ratio = Number.parseFloat(separator.getAttribute('aria-valuenow') ?? '50') / 100;
    splitResize = {
        separator,
        split,
        pointerId: event.pointerId,
        revision: currentCellRevision(),
        originalRatio: ratio,
        pointerX: event.clientX,
        pointerY: event.clientY,
    };
    document.body.classList.add('split-resizing');
    try {
        separator.setPointerCapture(event.pointerId);
    } catch {}
});

document.addEventListener(
    'pointermove',
    (event) => {
        if (!splitResize || event.pointerId !== splitResize.pointerId) {
            return;
        }
        if (currentCellRevision() !== splitResize.revision) {
            finishSplitResize(false);
            return;
        }
        event.preventDefault();
        splitResize.pointerX = event.clientX;
        splitResize.pointerY = event.clientY;
        applySplitRatio(
            splitResize.split,
            splitRatioAt(splitResize.split, event.clientX, event.clientY),
        );
    },
    { passive: false },
);

document.addEventListener('pointerup', (event) => {
    if (splitResize?.pointerId === event.pointerId) {
        event.preventDefault();
        splitResize.pointerX = event.clientX;
        splitResize.pointerY = event.clientY;
        finishSplitResize(true);
    }
});

document.addEventListener('pointercancel', (event) => {
    if (splitResize?.pointerId === event.pointerId) {
        finishSplitResize(false);
    }
});

document.addEventListener('keydown', (event) => {
    const separator = event.target.closest?.('.cell-split > .separator');
    if (!separator || separator.getAttribute('aria-disabled') === 'true') {
        return;
    }
    const horizontal = separator.getAttribute('aria-orientation') === 'vertical';
    const decrease = horizontal ? event.key === 'ArrowLeft' : event.key === 'ArrowUp';
    const increase = horizontal ? event.key === 'ArrowRight' : event.key === 'ArrowDown';
    if (!decrease && !increase && event.key !== 'Home' && event.key !== 'End') {
        return;
    }
    event.preventDefault();
    const split = separator.closest('.cell-split');
    const current = Number.parseFloat(separator.getAttribute('aria-valuenow') ?? '50') / 100;
    const ratio =
        event.key === 'Home'
            ? 0.15
            : event.key === 'End'
              ? 0.85
              : Math.min(0.85, Math.max(0.15, current + (increase ? 0.05 : -0.05)));
    rememberCellAnchor(separator);
    applySplitRatio(split, ratio);
    const revision = currentCellRevision();
    pendingCellRevision = revision + 1;
    emit(`cell-resize:${revision}:${split.dataset.cellSplit}:${ratio.toFixed(4)}`);
});

function cellTabsIn(list) {
    return [...list.querySelectorAll(':scope > .tab-entry > [data-cell-tab]')];
}

function cellInsertionIndex(list, pointerX) {
    const tabs = cellTabsIn(list);
    for (let index = 0; index < tabs.length; index++) {
        const rect = tabs[index].getBoundingClientRect();
        if (pointerX < rect.left + rect.width / 2) {
            return index;
        }
    }
    return tabs.length;
}

function cellPositionIn(group, pointerX, pointerY) {
    const tabs = group.querySelector(':scope > .cell-tabs');
    const tabList = tabs?.querySelector(':scope > .tab-list');
    if (tabs && tabList && pointerY <= tabs.getBoundingClientRect().bottom) {
        const index = cellInsertionIndex(tabList, pointerX);
        const tabItems = cellTabsIn(tabList);
        const groupRect = group.getBoundingClientRect();
        const insertionX =
            index < tabItems.length
                ? tabItems[index].getBoundingClientRect().left - groupRect.left
                : tabItems.length > 0
                  ? tabItems[tabItems.length - 1].getBoundingClientRect().right - groupRect.left
                  : tabList.getBoundingClientRect().left - groupRect.left;
        return {
            position: 'center',
            index,
            insertionX,
        };
    }

    const rect = group.getBoundingClientRect();
    const horizontal = Math.max(rect.width, 1);
    const vertical = Math.max(rect.height, 1);
    const distances = [
        ['left', (pointerX - rect.left) / horizontal],
        ['right', (rect.right - pointerX) / horizontal],
        ['top', (pointerY - rect.top) / vertical],
        ['bottom', (rect.bottom - pointerY) / vertical],
    ];
    distances.sort((first, second) => first[1] - second[1]);
    return distances[0][1] < 0.28
        ? { position: distances[0][0], index: 2147483647 }
        : { position: 'center', index: 2147483647 };
}

function findCellTarget(pointerX, pointerY) {
    for (const element of document.elementsFromPoint(pointerX, pointerY)) {
        const group = element.closest?.('[data-cell-group]');
        if (group) {
            if (group === cellDrag?.sourceGroup && cellDrag.sourceFloating) {
                const tabStrip = group.querySelector(':scope > .cell-tabs');
                const stripRect = tabStrip?.getBoundingClientRect();
                const insideTabStrip =
                    stripRect &&
                    pointerX >= stripRect.left &&
                    pointerX <= stripRect.right &&
                    pointerY >= stripRect.top &&
                    pointerY <= stripRect.bottom;
                if (!insideTabStrip) {
                    continue;
                }
            }
            const targetOwners = new Set(
                cellTabsIn(group.querySelector(':scope > .cell-tabs > .tab-list'))
                    .map((tab) => tab.dataset.cellOwner)
                    .filter(Boolean),
            );
            if (
                cellDrag?.cellId &&
                (targetOwners.size !== 1 || !targetOwners.has(cellDrag.cellId))
            ) {
                return { blocked: true };
            }
            const placement = cellPositionIn(group, pointerX, pointerY);
            return {
                id: group.dataset.cellGroup,
                preview: group.querySelector(':scope > .drop-preview'),
                ...placement,
            };
        }
        const region = element.closest?.('.is-empty[data-cell-region]');
        if (region) {
            if (cellDrag?.cellId && region.dataset.cellOwner !== cellDrag.cellId) {
                return { blocked: true };
            }
            return {
                id: region.dataset.cellRegion,
                position: 'center',
                index: 2147483647,
                preview: region.querySelector(':scope > .drop-preview'),
            };
        }
    }
    return null;
}

function clearCellPreview() {
    if (!cellPreview) {
        return;
    }
    cellPreview.classList.remove(
        'visible',
        'center',
        'left',
        'right',
        'top',
        'bottom',
        'tab-insert',
    );
    cellPreview.style.removeProperty('--cell-insertion-x');
    cellPreview = null;
}

function showCellPreview(target) {
    if (cellPreview !== target?.preview) {
        clearCellPreview();
    }
    if (!target?.preview) {
        return;
    }
    cellPreview = target.preview;
    cellPreview.classList.remove('center', 'left', 'right', 'top', 'bottom', 'tab-insert');
    cellPreview.style.removeProperty('--cell-insertion-x');
    if (target.insertionX !== undefined) {
        cellPreview.style.setProperty('--cell-insertion-x', `${target.insertionX}px`);
        cellPreview.classList.add('visible', 'tab-insert');
        return;
    }
    cellPreview.classList.add('visible', target.position);
}

function beginCellDrag(event) {
    cellDrag.started = true;
    cellDrag.source.classList.add('dragging');
    document.body.classList.add('drag-active');
    const ghost = document.createElement('div');
    ghost.className = 'drag-ghost';
    ghost.textContent = cellDrag.source.dataset.cellLabel || cellDrag.source.textContent;
    document.body.append(ghost);
    cellDrag.ghost = ghost;
    moveCellGhost(event.clientX, event.clientY);
}

function moveCellGhost(pointerX, pointerY) {
    if (!cellDrag?.ghost) {
        return;
    }
    cellDrag.ghost.style.transform = `translate3d(${pointerX + 12}px, ${pointerY + 12}px, 0)`;
}

function finishCellDrag() {
    clearCellPreview();
    if (!cellDrag) {
        return;
    }
    const drag = cellDrag;
    cellDrag = null;
    drag.source.classList.remove('dragging');
    drag.ghost?.remove();
    document.body.classList.remove('drag-active');
    try {
        drag.source.releasePointerCapture(drag.pointerId);
    } catch {}
}

document.addEventListener('pointerdown', (event) => {
    const source = event.target.closest?.('[data-cell-tab]');
    if (!source || !cellLayoutReady() || (event.pointerType === 'mouse' && event.button !== 0)) {
        return;
    }
    finishCellDrag();
    cellDrag = {
        source,
        sourceGroup: source.closest('[data-cell-group]'),
        sourceFloating: source.closest('[data-cell-floating]'),
        cellId: source.dataset.cellOwner,
        revision: currentCellRevision(),
        tabId: source.dataset.cellTab,
        pointerId: event.pointerId,
        startX: event.clientX,
        startY: event.clientY,
        started: false,
        ghost: null,
    };
    try {
        source.setPointerCapture(event.pointerId);
    } catch {}
});

document.addEventListener(
    'pointermove',
    (event) => {
        if (!cellDrag || event.pointerId !== cellDrag.pointerId) {
            return;
        }
        if (currentCellRevision() !== cellDrag.revision) {
            finishCellDrag();
            return;
        }
        if (!cellDrag.started) {
            const distance = Math.hypot(
                event.clientX - cellDrag.startX,
                event.clientY - cellDrag.startY,
            );
            if (distance < cellDragThreshold) {
                return;
            }
            beginCellDrag(event);
        }
        event.preventDefault();
        moveCellGhost(event.clientX, event.clientY);
        showCellPreview(findCellTarget(event.clientX, event.clientY));
    },
    { passive: false },
);

document.addEventListener('pointerup', (event) => {
    if (!cellDrag || event.pointerId !== cellDrag.pointerId) {
        return;
    }
    const { started, tabId, revision } = cellDrag;
    if (!started) {
        finishCellDrag();
        return;
    }

    event.preventDefault();
    const target = findCellTarget(event.clientX, event.clientY);
    suppressCellClick = true;
    finishCellDrag();
    if (target?.blocked) {
        setTimeout(() => {
            suppressCellClick = false;
        }, 0);
        return;
    }
    pendingCellRevision = revision + 1;
    if (target) {
        emit(`cell:${revision}:${tabId}:${target.id}:${target.position}:${target.index}`);
    } else {
        emit(
            `cell-detach:${revision}:${tabId}:${Math.round(event.clientX)}:${Math.round(event.clientY)}` +
                `:${window.innerWidth}:${window.innerHeight}`,
        );
    }
    setTimeout(() => {
        suppressCellClick = false;
    }, 0);
});

document.addEventListener('pointercancel', (event) => {
    if (cellDrag?.pointerId === event.pointerId) {
        finishCellDrag();
    }
});

document.addEventListener('lostpointercapture', (event) => {
    if (cellDrag?.pointerId === event.pointerId) {
        finishCellDrag();
    }
});

window.addEventListener('blur', () => {
    finishCellDrag();
    finishSplitResize(false);
});
document.addEventListener('visibilitychange', () => {
    if (document.hidden) {
        finishCellDrag();
    }
});

let normalizeCellFrame = 0;
window.addEventListener('resize', () => {
    cancelAnimationFrame(normalizeCellFrame);
    normalizeCellFrame = requestAnimationFrame(() => {
        normalizeCellFrame = 0;
        emit(`cell-normalize:${window.innerWidth}:${window.innerHeight}`);
    });
});

document.addEventListener(
    'click',
    (event) => {
        if (suppressCellClick && event.target.closest?.('[data-cell-tab]')) {
            event.preventDefault();
            event.stopImmediatePropagation();
        }
    },
    true,
);

document.addEventListener('keydown', (event) => {
    if (event.key === 'Escape' && cellDrag?.started) {
        event.preventDefault();
        finishCellDrag();
        return;
    }
    const current = event.target.closest?.('[data-cell-tab]');
    const list = current?.closest?.('.tab-list');
    if (!current || !list || !['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) {
        return;
    }
    const tabs = cellTabsIn(list);
    const currentIndex = tabs.indexOf(current);
    const targetIndex =
        event.key === 'Home'
            ? 0
            : event.key === 'End'
              ? tabs.length - 1
              : (currentIndex + (event.key === 'ArrowRight' ? 1 : -1) + tabs.length) % tabs.length;
    event.preventDefault();
    tabs[targetIndex].focus();
    tabs[targetIndex].click();
});

document.addEventListener(
    'wheel',
    (event) => {
        const list = event.target.closest?.('.tab-list');
        if (!list || list.scrollWidth <= list.clientWidth) {
            return;
        }
        list.scrollLeft += event.deltaY !== 0 ? event.deltaY : event.deltaX;
        event.preventDefault();
    },
    { passive: false },
);
