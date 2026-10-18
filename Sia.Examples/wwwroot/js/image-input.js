export const imageBudget = 32 * 1024 * 1024;

export async function decodeImage(file, signal) {
    if (!['image/png', 'image/jpeg', 'image/webp'].includes(file.type) || file.size > 16 * 1024 * 1024)
        throw new Error('Choose a PNG, JPEG or WebP image up to 16 MB.');
    const url = URL.createObjectURL(file);
    const image = new Image();
    const canvas = document.createElement('canvas');
    try {
        await new Promise((resolve, reject) => {
            const abort = () => { image.src = ''; reject(new DOMException('Image selection cancelled.', 'AbortError')); };
            const finish = action => { signal.removeEventListener('abort', abort); action(); };
            image.onload = () => finish(resolve);
            image.onerror = () => finish(() => reject(new Error('The image could not be decoded.')));
            if (signal.aborted) { abort(); return; }
            signal.addEventListener('abort', abort, { once: true });
            image.src = url;
        });
        signal.throwIfAborted();
        const width = image.naturalWidth, height = image.naturalHeight;
        if (!width || !height || width > 4096 || height > 4096 || width * height > 4 * 1024 * 1024)
            throw new Error('Image dimensions must be at most 4096 per side and 4 megapixels.');
        canvas.width = width;
        canvas.height = height;
        const context = canvas.getContext('2d', { willReadFrequently: true });
        context.drawImage(image, 0, 0);
        const pixels = new Uint8Array(context.getImageData(0, 0, width, height).data.buffer);
        const scale = Math.min(1, 64 / Math.max(width, height));
        canvas.width = Math.max(1, Math.round(width * scale));
        canvas.height = Math.max(1, Math.round(height * scale));
        context.drawImage(image, 0, 0, canvas.width, canvas.height);
        return { name: file.name, width, height, pixels, preview: canvas.toDataURL('image/png') };
    }
    finally {
        image.onload = image.onerror = null;
        image.src = '';
        canvas.width = canvas.height = 0;
        URL.revokeObjectURL(url);
    }
}

export function createImageControl(binding, field, name) {
    const row = document.createElement('div');
    row.className = 'editor-uniform-field';
    row.setAttribute('role', 'group');
    row.setAttribute('aria-label', field.name);
    const label = document.createElement('div');
    label.className = 'editor-uniform-label';
    const title = document.createElement('span');
    title.textContent = name;
    const input = document.createElement('input');
    input.type = 'file';
    input.accept = 'image/png,image/jpeg,image/webp';
    input.hidden = true;
    const choose = document.createElement('button');
    choose.type = 'button';
    choose.className = 'btn';
    choose.textContent = 'Choose image';
    choose.setAttribute('aria-label', `${field.name} image`);
    choose.addEventListener('click', () => input.click());
    label.append(title, choose);
    const preview = document.createElement('img');
    preview.className = 'editor-uniform-image';
    preview.alt = `${field.name} preview`;
    const status = document.createElement('span');
    const error = document.createElement('span');
    error.className = 'editor-uniform-error';
    error.setAttribute('role', 'status');
    const clear = document.createElement('button');
    clear.type = 'button';
    clear.className = 'btn';
    clear.textContent = 'Clear';
    clear.setAttribute('aria-label', `Clear ${field.name}`);
    label.append(clear);
    const refresh = () => {
        preview.hidden = !field.image;
        if (field.image) preview.src = field.image.preview;
        else preview.removeAttribute('src');
        status.textContent = field.abort ? 'Loading image…' : field.image ? `${field.image.name} · ${field.image.width} × ${field.image.height}` : 'No image';
        clear.disabled = !field.image && !field.abort;
    };
    input.addEventListener('change', async () => {
        const file = input.files?.[0];
        if (!file) return;
        field.abort?.abort();
        const request = field.abort = new AbortController();
        error.textContent = '';
        refresh();
        try {
            const image = await decodeImage(file, request.signal);
            if (field.abort === request && binding.owner.active === binding) binding.updateImage(field, image);
        }
        catch (failure) { if (field.abort === request && failure.name !== 'AbortError') error.textContent = failure.message; }
        finally {
            if (field.abort === request) { field.abort = null; input.value = ''; refresh(); }
        }
    });
    clear.addEventListener('click', () => {
        field.abort?.abort();
        field.abort = null;
        binding.updateImage(field, null);
        error.textContent = '';
        refresh();
    });
    row.append(label, input, preview, status, error);
    refresh();
    return row;
}
