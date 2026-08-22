document.getElementById('boot-reload')?.addEventListener('click', () => {
    window.location.reload();
});

let expectedTotal = 0;

function countInitialResources(resources) {
    if (!resources) {
        return 0;
    }
    const size = (group) => (Array.isArray(group) ? group.length : 0);
    return (
        size(resources.wasmNative) +
        size(resources.jsModuleNative) +
        size(resources.jsModuleRuntime) +
        Math.min(1, size(resources.icu)) +
        size(resources.coreAssembly) +
        size(resources.assembly)
    );
}

function updateBootProgress(loaded, total) {
    const overlay = document.getElementById('boot-overlay');
    if (!overlay) {
        return;
    }
    const percent = total === 0 ? 0 : Math.min(100, Math.floor((loaded / total) * 100));
    overlay
        .querySelector('.boot-progress-fill')
        ?.style.setProperty('--boot-progress-share', `${percent}%`);
    const label = overlay.querySelector('.boot-progress-label');
    if (label) {
        label.textContent = `${loaded} / ${total} (${percent}%)`;
    }
}

export const bootModuleConfig = {
    onConfigLoaded(config) {
        expectedTotal = countInitialResources(config?.resources);
        updateBootProgress(0, expectedTotal);
    },
    onDownloadResourceProgress(loaded, queuedTotal) {
        expectedTotal = Math.max(expectedTotal, queuedTotal);
        updateBootProgress(loaded, expectedTotal);
    },
};

export function dismissBootOverlay() {
    const overlay = document.getElementById('boot-overlay');
    if (!overlay) {
        return;
    }
    overlay.classList.add('done');
    overlay.addEventListener('transitionend', () => overlay.remove(), { once: true });
    setTimeout(() => overlay.remove(), 600);
}

export function showBootError(message) {
    const overlay = document.getElementById('boot-overlay');
    const banner = document.createElement('div');
    banner.className = 'boot-error-banner';
    banner.textContent = message;
    document.body.prepend(banner);
    if (!overlay) {
        return;
    }
    overlay.classList.add('failed');
    overlay.querySelector('.boot-spinner')?.setAttribute('aria-hidden', 'true');
    const text = overlay.querySelector('.boot-message');
    if (text) {
        text.textContent = 'The runtime could not be downloaded. Check your connection and reload.';
    }
    overlay.querySelector('.boot-reload')?.removeAttribute('hidden');
}
