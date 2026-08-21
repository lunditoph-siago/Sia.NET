import { emit } from './notebook-runtime.js';

document.getElementById('sidebar-toggle')?.addEventListener('click', () => {
    document.getElementById('app')?.classList.toggle('sidebar-open');
});

document.getElementById('sidebar')?.addEventListener('click', (event) => {
    if (
        event.target.closest?.('.example-btn, [data-file-tree-open]') &&
        matchMedia('(max-width: 899px)').matches
    ) {
        document.getElementById('app')?.classList.remove('sidebar-open');
    }
});

const sidebarTabs = [...document.querySelectorAll('[data-sidebar-tab]')];

function selectSidebarView(view, focus = false) {
    const sidebar = document.getElementById('sidebar');
    if (!sidebar || !sidebarTabs.some((tab) => tab.dataset.sidebarTab === view)) {
        return;
    }
    sidebar.dataset.sidebarView = view;
    for (const tab of sidebarTabs) {
        const selected = tab.dataset.sidebarTab === view;
        tab.classList.toggle('active', selected);
        tab.setAttribute('aria-selected', selected ? 'true' : 'false');
        tab.tabIndex = selected ? 0 : -1;
        document
            .getElementById(tab.getAttribute('aria-controls') ?? '')
            ?.classList.toggle('hidden', !selected);
        if (selected && focus) {
            tab.focus();
        }
    }
}

document.getElementById('sidebar')?.addEventListener('click', (event) => {
    const tab = event.target.closest?.('[data-sidebar-tab]');
    if (tab) {
        selectSidebarView(tab.dataset.sidebarTab);
    }
});

document.getElementById('sidebar')?.addEventListener('keydown', (event) => {
    const tab = event.target.closest?.('[data-sidebar-tab]');
    if (!tab || !['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) {
        return;
    }
    event.preventDefault();
    const index = sidebarTabs.indexOf(tab);
    const next =
        event.key === 'Home'
            ? 0
            : event.key === 'End'
              ? sidebarTabs.length - 1
              : (index + (event.key === 'ArrowRight' ? 1 : -1) + sidebarTabs.length) %
                sidebarTabs.length;
    selectSidebarView(sidebarTabs[next].dataset.sidebarTab, true);
});

document.getElementById('sidebar')?.addEventListener('click', (event) => {
    const toggle = event.target.closest?.('[data-library-toggle]');
    if (!toggle) {
        return;
    }
    const items = document.getElementById(toggle.dataset.libraryToggle);
    const expanded = toggle.getAttribute('aria-expanded') !== 'false';
    toggle.setAttribute('aria-expanded', expanded ? 'false' : 'true');
    items?.classList.toggle('hidden', expanded);
});

function beginFileTreeRename(trigger) {
    const entry = trigger.closest('[data-file-tree-entry]');
    const input = entry?.querySelector('[data-file-tree-rename]');
    if (!entry || !input) {
        return;
    }
    entry.classList.add('renaming');
    input.value = input.dataset.savedValue ?? '';
    input.focus();
    input.select();
}

function finishFileTreeRename(input, save) {
    const entry = input.closest('[data-file-tree-entry]');
    if (!entry?.classList.contains('renaming')) {
        return;
    }
    entry.classList.remove('renaming');
    let value = input.value.trim();
    if (value.toLowerCase().endsWith('.cs')) {
        value = value.slice(0, -3).trim();
    }
    const savedValue = input.dataset.savedValue ?? '';
    if (save && value !== savedValue) {
        emit(`rename-script:${input.dataset.fileTreeRename}:${encodeURIComponent(value)}`);
    } else {
        input.value = savedValue;
    }
    input.blur();
}

const fileTreeFolderState = new Map();
const fileTreeRoot = document.getElementById('notebook-files');
if (!fileTreeRoot) {
    throw new Error('The sidebar #notebook-files tree is required');
}

fileTreeRoot.addEventListener(
    'toggle',
    (event) => {
        const folder = event.target.closest?.('[data-file-tree-folder]');
        if (folder) {
            fileTreeFolderState.set(folder.dataset.fileTreeFolder, folder.open);
        }
    },
    true,
);

new MutationObserver(() => {
    for (const folder of fileTreeRoot.querySelectorAll('[data-file-tree-folder]')) {
        const open = fileTreeFolderState.get(folder.dataset.fileTreeFolder);
        if (open !== undefined) {
            folder.open = open;
        }
    }
}).observe(fileTreeRoot, { childList: true, subtree: true });

document.getElementById('sidebar')?.addEventListener('click', (event) => {
    const rename = event.target.closest?.('[data-file-tree-rename-trigger]');
    if (rename) {
        beginFileTreeRename(rename);
        return;
    }
    const open = event.target.closest?.('[data-file-tree-open]');
    if (open) {
        requestAnimationFrame(() => {
            const tab = document.getElementById(open.dataset.fileTreeOpen);
            tab?.closest('[data-cell-region]')?.scrollIntoView({
                block: 'center',
                inline: 'nearest',
            });
        });
    }
});

document.getElementById('sidebar')?.addEventListener('keydown', (event) => {
    const input = event.target.closest?.('[data-file-tree-rename]');
    if (!input) {
        return;
    }
    if (event.key === 'Enter') {
        event.preventDefault();
        finishFileTreeRename(input, true);
    } else if (event.key === 'Escape') {
        event.preventDefault();
        finishFileTreeRename(input, false);
    }
});

document.getElementById('sidebar')?.addEventListener('focusout', (event) => {
    const input = event.target.closest?.('[data-file-tree-rename]');
    if (input) {
        finishFileTreeRename(input, true);
    }
});

const inlineEditScope =
    '.notebook-titlebar, .section-heading-editor, .paragraph-editor, .scope-editor';

function beginInlineEdit(inputId) {
    const input = document.getElementById(inputId);
    if (!input) {
        return;
    }
    input.closest(inlineEditScope)?.classList.add('is-editing');
    input.focus();
    input.select();
}

function updateInlineSummary(input) {
    const summary = document.getElementById(input.dataset.inlineSummary ?? '');
    if (!summary) {
        return;
    }
    const value = input.value.trim();
    summary.textContent = value
        ? `${input.dataset.inlinePrefix ?? ''}${value}`
        : (input.dataset.inlineEmptyLabel ?? '');
}

const overlayRoot = document.getElementById('overlay-root');
if (!overlayRoot) {
    throw new Error('The page-level #overlay-root is required');
}

const menuPortals = new Map();
const menuPortalOwners = new WeakMap();

export function menuOwner(target) {
    const directOwner = target.closest?.('.menu-toggle');
    if (directOwner) {
        return directOwner;
    }
    const popover = target.closest?.('.menu-popover.is-portaled');
    return popover ? (menuPortalOwners.get(popover) ?? null) : null;
}

function positionMenuPortal(owner) {
    const state = menuPortals.get(owner);
    const summary = owner.querySelector(':scope > summary');
    if (!state || !summary?.isConnected) {
        return;
    }

    const { popover } = state;
    const margin = 8;
    const gap = 4;
    const viewportWidth = document.documentElement.clientWidth;
    const viewportHeight = document.documentElement.clientHeight;
    const anchor = summary.getBoundingClientRect();

    popover.style.visibility = 'hidden';
    popover.style.maxHeight = `${Math.max(0, viewportHeight - margin * 2)}px`;
    const width = popover.offsetWidth;
    const height = popover.offsetHeight;
    const left = Math.max(margin, Math.min(anchor.right - width, viewportWidth - margin - width));
    const fitsBelow = anchor.bottom + gap + height <= viewportHeight - margin;
    const top = fitsBelow ? anchor.bottom + gap : Math.max(margin, anchor.top - gap - height);

    popover.style.left = `${left}px`;
    popover.style.top = `${top}px`;
    popover.style.visibility = '';
}

function openMenuPortal(owner) {
    if (menuPortals.has(owner)) {
        positionMenuPortal(owner);
        return;
    }
    const popover = owner.querySelector(':scope > .menu-popover');
    if (!popover) {
        return;
    }

    const placeholder = document.createComment('menu-popover');
    popover.replaceWith(placeholder);
    popover.classList.add('is-portaled');
    overlayRoot.append(popover);
    menuPortals.set(owner, { popover, placeholder });
    menuPortalOwners.set(popover, owner);
    positionMenuPortal(owner);
}

function closeMenuPortal(owner) {
    const state = menuPortals.get(owner);
    if (!state) {
        return;
    }

    const { popover, placeholder } = state;
    if (placeholder.parentNode) {
        placeholder.replaceWith(popover);
    } else {
        popover.remove();
    }
    popover.classList.remove('is-portaled');
    popover.style.removeProperty('left');
    popover.style.removeProperty('top');
    popover.style.removeProperty('max-height');
    popover.style.removeProperty('visibility');
    menuPortalOwners.delete(popover);
    menuPortals.delete(owner);
}

function positionOpenMenuPortals() {
    for (const owner of menuPortals.keys()) {
        positionMenuPortal(owner);
    }
}

let menuPositionFrame = 0;
function scheduleMenuPortalPosition() {
    if (menuPortals.size === 0 || menuPositionFrame !== 0) {
        return;
    }
    menuPositionFrame = requestAnimationFrame(() => {
        menuPositionFrame = 0;
        positionOpenMenuPortals();
    });
}

document.addEventListener(
    'toggle',
    (event) => {
        const owner = event.target;
        if (!(owner instanceof HTMLDetailsElement) || !owner.classList.contains('menu-toggle')) {
            return;
        }
        if (owner.open) {
            openMenuPortal(owner);
        } else {
            closeMenuPortal(owner);
        }
    },
    true,
);

window.addEventListener('resize', scheduleMenuPortalPosition);
window.addEventListener('scroll', scheduleMenuPortalPosition, true);
window.visualViewport?.addEventListener('resize', scheduleMenuPortalPosition);
window.visualViewport?.addEventListener('scroll', scheduleMenuPortalPosition);

new MutationObserver(() => {
    for (const [owner, { popover }] of menuPortals) {
        if (!owner.isConnected || popover.parentElement !== overlayRoot) {
            owner.removeAttribute('open');
            closeMenuPortal(owner);
        }
    }
}).observe(document.body, { childList: true, subtree: true });

document.addEventListener('focusin', (event) => {
    const input = event.target.closest?.('[data-inline-input]');
    input?.closest(inlineEditScope)?.classList.add('is-editing');
});

document.addEventListener('focusout', (event) => {
    const input = event.target.closest?.('[data-inline-input]');
    if (!input || input.value !== (input.dataset.savedValue ?? '')) {
        return;
    }
    input.closest(inlineEditScope)?.classList.remove('is-editing', 'is-dirty');
});

document.addEventListener('input', (event) => {
    const input = event.target.closest?.('[data-inline-input]');
    if (!input) {
        return;
    }
    input
        .closest(inlineEditScope)
        ?.classList.toggle('is-dirty', input.value !== input.dataset.savedValue);
});

document.addEventListener('keydown', (event) => {
    const input = event.target.closest?.('[data-inline-input]');
    if (!input || event.isComposing) {
        return;
    }
    const saveRequested =
        event.key === 'Enter' && (input.tagName !== 'TEXTAREA' || event.ctrlKey || event.metaKey);
    if (saveRequested) {
        const save = document.querySelector(`[data-inline-save="${CSS.escape(input.id)}"]`);
        if (save && (input.value.trim() || input.dataset.allowEmpty)) {
            event.preventDefault();
            save.click();
        }
        return;
    }
    if (event.key === 'Escape') {
        const discard = document.querySelector(`[data-inline-discard="${CSS.escape(input.id)}"]`);
        if (discard) {
            event.preventDefault();
            discard.click();
        }
    }
});

document.addEventListener('click', (event) => {
    const begin = event.target.closest?.('[data-inline-begin]');
    if (begin) {
        beginInlineEdit(begin.dataset.inlineBegin);
    }
    const save = event.target.closest?.('[data-inline-save]');
    if (save) {
        const input = document.getElementById(save.dataset.inlineSave);
        if (input && (input.value.trim() || input.dataset.allowEmpty)) {
            const savedValue = input.dataset.inlineTrim
                ? input.value.trim()
                : input.dataset.allowEmpty
                  ? input.value
                  : input.value.trim();
            input.value = savedValue;
            input.dataset.savedValue = savedValue;
            updateInlineSummary(input);
            input.closest(inlineEditScope)?.classList.remove('is-editing', 'is-dirty');
            input.blur();
        }
    }
    const discard = event.target.closest?.('[data-inline-discard]');
    if (discard) {
        const input = document.getElementById(discard.dataset.inlineDiscard);
        if (input) {
            input.value = input.dataset.savedValue ?? '';
            input.closest(inlineEditScope)?.classList.remove('is-editing', 'is-dirty');
            input.blur();
        }
    }
    if (event.target.closest?.('.menu-item')) {
        menuOwner(event.target)?.removeAttribute('open');
    }

    const currentMenu = menuOwner(event.target);
    for (const menu of document.querySelectorAll('.menu-toggle[open]')) {
        if (menu !== currentMenu) {
            menu.removeAttribute('open');
        }
    }
});

document.addEventListener('keydown', (event) => {
    const menu = menuOwner(event.target);
    if (menu?.open) {
        const state = menuPortals.get(menu);
        const summary = menu.querySelector(':scope > summary');
        const items = [...(state?.popover.querySelectorAll('.menu-item') ?? [])];
        const itemIndex = items.indexOf(event.target.closest?.('.menu-item'));
        if (event.key === 'Escape') {
            event.preventDefault();
            menu.removeAttribute('open');
            summary?.focus();
            return;
        }
        if (event.key === 'Tab' && !event.shiftKey && event.target === summary) {
            event.preventDefault();
            items[0]?.focus();
            return;
        }
        if (event.key === 'Tab' && event.shiftKey && itemIndex === 0) {
            event.preventDefault();
            summary?.focus();
            return;
        }
        if (event.key === 'Tab' && !event.shiftKey && itemIndex === items.length - 1) {
            event.preventDefault();
            summary?.focus();
            return;
        }
        if ((event.key === 'ArrowDown' || event.key === 'ArrowUp') && items.length > 0) {
            event.preventDefault();
            const step = event.key === 'ArrowDown' ? 1 : -1;
            const nextIndex =
                itemIndex < 0
                    ? step > 0
                        ? 0
                        : items.length - 1
                    : (itemIndex + step + items.length) % items.length;
            items[nextIndex].focus();
            return;
        }
    }

    const script = event.target.closest?.('[data-script-id]');
    const control = (event.ctrlKey || event.metaKey) && !event.altKey;
    if (!script || !control || event.isComposing) {
        return;
    }
    if (event.key === 'Enter') {
        event.preventDefault();
        emit(`toggle-run:${script.dataset.scriptId}`);
    } else if (event.key.toLowerCase() === 's' && script.dataset.scriptEditable === 'true') {
        event.preventDefault();
        emit(`save:${script.dataset.scriptId}`);
    }
});

document.getElementById('packages-toggle')?.addEventListener('click', (event) => {
    event.stopPropagation();
    document.getElementById('header-packages')?.classList.toggle('open');
});

document.addEventListener('click', (event) => {
    const packages = document.getElementById('header-packages');
    if (packages?.classList.contains('open') && !packages.contains(event.target)) {
        packages.classList.remove('open');
    }
});
