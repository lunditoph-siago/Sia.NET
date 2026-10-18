import { emit } from './notebook-runtime.js';

const compactSidebar = matchMedia('(max-width: 899px)');
let sidebarReturnFocus;

function syncSidebar() {
    const open = !compactSidebar.matches || document.getElementById('app')?.classList.contains('sidebar-open');
    const sidebar = document.getElementById('sidebar');
    if (sidebar) sidebar.inert = !open;
    for (const button of document.querySelectorAll('#sidebar-toggle, [data-sidebar-toggle]')) {
        if (button.getAttribute('aria-expanded') !== String(open)) button.setAttribute('aria-expanded', String(open));
    }
}

function closeSidebar(focus = false) {
    document.getElementById('app')?.classList.remove('sidebar-open');
    for (const menu of document.querySelectorAll('#sidebar .menu-toggle[open]')) menu.open = false;
    syncSidebar();
    if (focus && sidebarReturnFocus?.isConnected) sidebarReturnFocus.focus();
}

compactSidebar.addEventListener('change', () => { closeSidebar(); });
syncSidebar();

document.addEventListener('click', (event) => {
    const toggle = event.target.closest?.('#sidebar-toggle, [data-sidebar-toggle]');
    if (toggle) {
        sidebarReturnFocus = toggle;
        document.getElementById('app')?.classList.toggle('sidebar-open');
        syncSidebar();
        if (document.getElementById('app')?.classList.contains('sidebar-open')) {
            if (toggle.hasAttribute('data-sidebar-toggle')) selectSidebarView('files');
            if (document.getElementById('sidebar')?.dataset.sidebarView === 'workspace') focusWorkspaceFiles();
            else document.querySelector('#sidebar .sidebar-tab.active')?.focus();
        }
    } else if (compactSidebar.matches && document.getElementById('app')?.classList.contains('sidebar-open')) {
        if (event.target.closest?.('[data-sidebar-dismiss]')) closeSidebar(true);
        else if (!event.target.closest?.('#sidebar') && !menuOwner(event.target)?.closest('#sidebar')) closeSidebar();
    }
});

document.addEventListener('keydown', (event) => {
    if (event.key === 'Escape' && compactSidebar.matches && document.getElementById('app')?.classList.contains('sidebar-open') &&
        !event.target.closest?.('[data-file-tree-rename]') && !menuOwner(event.target)?.open) {
        event.preventDefault();
        closeSidebar(true);
    }
});

document.addEventListener('keydown', (event) => {
    if (!document.querySelector('.editor-page')) {
        return;
    }
    if (event.key.toLowerCase() === 's' && (event.ctrlKey || event.metaKey)) {
        event.preventDefault();
        emit(event.shiftKey ? 'editor-page-save-all' : 'editor-page-save');
    } else if (event.key.toLowerCase() === 'w' && event.altKey) {
        event.preventDefault();
        emit('editor-page-close-active-tab');
    }
});

document.getElementById('sidebar')?.addEventListener('click', (event) => {
    if (
        event.target.closest?.('.example-btn, [data-file-tree-open]') &&
        matchMedia('(max-width: 899px)').matches
    ) {
        closeSidebar(true);
    }
});

const sidebarTabs = [...document.querySelectorAll('[data-sidebar-tab]')];

function selectSidebarView(view, focus = false) {
    const sidebar = document.getElementById('sidebar');
    if (sidebar?.dataset.sidebarView === 'workspace') return;
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
    const entry = (menuOwner(trigger) ?? trigger).closest('[data-file-tree-entry]');
    const input = entry?.querySelector(':scope > [data-file-tree-rename]');
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
    if (!input.dataset.fileTreeRenameKeepExtension && value.toLowerCase().endsWith('.cs')) {
        value = value.slice(0, -3).trim();
    }
    const savedValue = input.dataset.savedValue ?? '';
    if (save && value !== savedValue) {
        const eventName = input.dataset.fileTreeRenameEvent ?? 'rename-script';
        emit(`${eventName}:${input.dataset.fileTreeRename}:${encodeURIComponent(value)}`);
    } else {
        input.value = savedValue;
    }
    entry.querySelector('.file-tree-file-open, .file-tree-folder-label')?.focus();
}

const fileTreeFolderState = new Map();
const fileTreeRoot = document.getElementById('notebook-files');
let fileTreeActiveKey;
if (!fileTreeRoot) {
    throw new Error('The sidebar #notebook-files tree is required');
}

function setFileTreeFolderOpen(folder, open) {
    const active = fileTreeRoot.querySelector('[aria-current="true"]');
    const activeKey = active?.closest('[data-file-tree-entry]')?.dataset.fileTreeEntry;
    if (activeKey) fileTreeActiveKey = activeKey;
    folder.open = open;
    fileTreeFolderState.set(folder.dataset.fileTreeFolder, open);
}

fileTreeRoot.addEventListener('click', (event) => {
    const label = event.target.closest?.('.file-tree-folder-label');
    const folder = label?.parentElement;
    if (!folder?.hasAttribute('data-file-tree-folder')) return;
    event.preventDefault();
    setFileTreeFolderOpen(folder, !folder.open);
});

new MutationObserver((records) => {
    const folders = new Set();
    let structureChanged = false;
    let selectionChanged = false;
    for (const record of records) {
        if (record.type === 'childList') {
            structureChanged = selectionChanged = true;
            for (const node of record.addedNodes) {
                if (!(node instanceof Element)) continue;
                if (node.hasAttribute('data-file-tree-folder')) folders.add(node);
                for (const folder of node.querySelectorAll('[data-file-tree-folder]')) folders.add(folder);
            }
        } else if (record.attributeName === 'aria-current') {
            selectionChanged = true;
        } else if (record.target.hasAttribute('data-file-tree-folder')) {
            folders.add(record.target);
        }
    }
    for (const folder of folders) {
        const open = fileTreeFolderState.get(folder.dataset.fileTreeFolder);
        if (open !== undefined && folder.open !== open) {
            folder.open = open;
        }
    }
    if (selectionChanged) {
        const active = fileTreeRoot.querySelector('[aria-current="true"]');
        const activeKey = active?.closest('[data-file-tree-entry]')?.dataset.fileTreeEntry;
        if (activeKey && activeKey !== fileTreeActiveKey) {
            for (let folder = active.closest('[data-file-tree-folder]'); folder;
                folder = folder.parentElement.closest('[data-file-tree-folder]')) {
                setFileTreeFolderOpen(folder, true);
            }
        }
        if (activeKey) fileTreeActiveKey = activeKey;
    }
    if (structureChanged) syncSidebar();
}).observe(fileTreeRoot, { childList: true, subtree: true, attributes: true, attributeFilter: ['aria-current', 'open'] });

document.addEventListener('click', (event) => {
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
    if (!input || event.isComposing) {
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

function fileTreeEntries() {
    return [...fileTreeRoot.querySelectorAll('.file-tree-file-open, .file-tree-folder-label')]
        .filter((entry) => {
            for (let parent = entry.parentElement.closest('details'); parent;
                parent = parent.parentElement.closest('details')) {
                if (!parent.open && parent.querySelector(':scope > summary') !== entry) return false;
            }
            return !entry.closest('.renaming');
        });
}

function focusWorkspaceFiles() {
    const entries = fileTreeEntries();
    const active = entries.find((entry) => entry.getAttribute('aria-current') === 'true');
    (active ?? entries[0] ?? fileTreeRoot.querySelector('.file-tree-tools button'))?.focus();
}

fileTreeRoot.addEventListener('keydown', (event) => {
    const target = event.target.closest?.('.file-tree-file-open, .file-tree-folder-label');
    if (!target || event.altKey || event.ctrlKey || event.metaKey || event.isComposing) return;
    if (event.key === 'F2') {
        event.preventDefault();
        beginFileTreeRename(target);
        return;
    }
    const folder = target.matches('.file-tree-folder-label') ? target.parentElement : null;
    const entries = fileTreeEntries();
    const index = entries.indexOf(target);
    let next;
    if (event.key === 'ArrowDown') next = entries[Math.min(index + 1, entries.length - 1)];
    else if (event.key === 'ArrowUp') next = entries[Math.max(index - 1, 0)];
    else if (event.key === 'Home') next = entries[0];
    else if (event.key === 'End') next = entries.at(-1);
    else if (event.key === 'ArrowRight' && folder) {
        if (!folder.open) setFileTreeFolderOpen(folder, true);
        else next = entries[index + 1];
    } else if (event.key === 'ArrowLeft') {
        if (folder?.open) setFileTreeFolderOpen(folder, false);
        else next = target.closest('.file-tree-children')?.parentElement.querySelector(':scope > summary');
    } else return;
    event.preventDefault();
    next?.focus();
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
