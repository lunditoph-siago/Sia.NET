const keys = {
    Space: 32,
    Quote: 39,
    Comma: 44,
    Minus: 45,
    Period: 46,
    Slash: 47,
    Semicolon: 59,
    Equal: 61,
    BracketLeft: 91,
    Backslash: 92,
    BracketRight: 93,
    Backquote: 96,
    IntlBackslash: 161,
    IntlRo: 162,
    Escape: 256,
    Enter: 257,
    Tab: 258,
    Backspace: 259,
    Insert: 260,
    Delete: 261,
    ArrowRight: 262,
    ArrowLeft: 263,
    ArrowDown: 264,
    ArrowUp: 265,
    PageUp: 266,
    PageDown: 267,
    Home: 268,
    End: 269,
    CapsLock: 280,
    ScrollLock: 281,
    NumLock: 282,
    PrintScreen: 283,
    Pause: 284,
    NumpadDecimal: 330,
    NumpadDivide: 331,
    NumpadMultiply: 332,
    NumpadSubtract: 333,
    NumpadAdd: 334,
    NumpadEnter: 335,
    NumpadEqual: 336,
    ShiftLeft: 340,
    ControlLeft: 341,
    AltLeft: 342,
    MetaLeft: 343,
    ShiftRight: 344,
    ControlRight: 345,
    AltRight: 346,
    MetaRight: 347,
    ContextMenu: 348,
};
function keyCode(code) {
    if (/^Key[A-Z]$/.test(code)) return code.charCodeAt(3);
    if (/^Digit[0-9]$/.test(code)) return code.charCodeAt(5);
    if (/^Numpad[0-9]$/.test(code)) return 320 + Number(code.slice(6));
    if (/^F\d+$/.test(code)) return 289 + Number(code.slice(1));
    return keys[code] ?? -1;
}
function modifiers(event) {
    return (
        (event.shiftKey ? 1 : 0) |
        (event.ctrlKey ? 2 : 0) |
        (event.altKey ? 4 : 0) |
        (event.metaKey ? 8 : 0) |
        (event.getModifierState?.('CapsLock') ? 16 : 0) |
        (event.getModifierState?.('NumLock') ? 32 : 0)
    );
}
const inputKinds = {
    move: 0,
    wheel: 1,
    enter: 2,
    exit: 3,
    press: 4,
    release: 5,
    keydown: 6,
    repeat: 7,
    keyup: 8,
    text: 9,
};
const normalCanvases = new Map();
export function createPipelineCanvas(module, containerId, canvasId, thread, normalCanvas) {
    const container = document.getElementById(containerId);
    if (!container) throw new Error('Pipeline panel is closed');
    const canvas = document.createElement('canvas');
    canvas.id = canvasId;
    canvas.tabIndex = 0;
    canvas.setAttribute('aria-label', 'Programmable pipeline canvas');
    canvas.className = 'editor-shader-canvas';
    container.append(canvas);
    const owner = {
        canvas,
        inputs: [],
        abort: new AbortController(),
        keys: new Set(),
        buttons: new Set(),
        stopped: false,
        raf: 0,
    };
    try {
        if (normalCanvas) {
            owner.context = canvas.getContext('2d', { alpha: false });
            if (!owner.context) throw new Error('Ordinary canvas context is unavailable');
            normalCanvases.set(canvasId, owner);
            owner.ready = module.siaEditorReadbackCanvas(canvas, thread, owner.abort.signal, presentPipelinePixels);
        } else owner.ready = module.siaEditorTransferCanvas(canvas, thread, owner.abort.signal);
    } catch (error) {
        normalCanvases.delete(canvasId);
        owner.abort.abort();
        canvas.remove();
        throw error;
    }
    const listen = (name, callback, options = {}) =>
        canvas.addEventListener(name, callback, {
            ...options,
            signal: owner.abort.signal,
        });
    const send = (kind, x = 0, y = 0, code = 0, mods = 0) => {
        if (!owner.stopped) owner.inputs.push(inputKinds[kind], x, y, code, mods);
    };
    listen('pointermove', (event) => {
        const box = canvas.getBoundingClientRect();
        send('move', event.clientX - box.left, event.clientY - box.top);
    });
    const button = (value) => (value === 1 ? 2 : value === 2 ? 1 : value);
    listen('pointerdown', (event) => {
        canvas.focus();
        canvas.setPointerCapture(event.pointerId);
        owner.buttons.add(button(event.button));
        send('press', 0, 0, button(event.button), modifiers(event));
    });
    listen('pointerup', (event) => {
        owner.buttons.delete(button(event.button));
        send('release', 0, 0, button(event.button), modifiers(event));
    });
    listen('pointerenter', () => send('enter'));
    listen('pointerleave', () => send('exit'));
    listen('contextmenu', (event) => event.preventDefault());
    listen(
        'wheel',
        (event) => {
            event.preventDefault();
            const factor =
                event.deltaMode === 1 ? 16 : event.deltaMode === 2 ? canvas.clientHeight : 1;
            send('wheel', -event.deltaX * factor, -event.deltaY * factor);
        },
        { passive: false },
    );
    listen('keydown', (event) => {
        const code = keyCode(event.code);
        owner.keys.add(code);
        send(event.repeat ? 'repeat' : 'keydown', 0, 0, code, modifiers(event));
        if (!event.ctrlKey && !event.metaKey && !event.altKey && [...event.key].length === 1)
            send('text', 0, 0, event.key.codePointAt(0));
        if (event.code === 'Space' || event.code.startsWith('Arrow')) event.preventDefault();
    });
    listen('keyup', (event) => {
        const code = keyCode(event.code);
        owner.keys.delete(code);
        send('keyup', 0, 0, code, modifiers(event));
    });
    const release = () => {
        for (const code of owner.keys) send('keyup', 0, 0, code);
        for (const code of owner.buttons) send('release', 0, 0, code);
        owner.keys.clear();
        owner.buttons.clear();
    };
    listen('blur', release);
    listen('pointercancel', release);
    document.addEventListener(
        'visibilitychange',
        () => {
            if (document.hidden) release();
        },
        { signal: owner.abort.signal },
    );
    return owner;
}
export function waitPipelineCanvas(owner) {
    return owner.ready;
}
export function presentPipelinePixels(selector, pixels, width, height) {
    const owner = normalCanvases.get(selector.slice(1));
    if (!owner || owner.stopped) return;
    if (owner.canvas.width !== width || owner.canvas.height !== height) {
        owner.canvas.width = width;
        owner.canvas.height = height;
        owner.image = new ImageData(width, height);
    }
    owner.image ??= new ImageData(width, height);
    owner.image.data.set(pixels);
    owner.context.putImageData(owner.image, 0, 0);
}
export function nextPipelineFrame(owner) {
    return new Promise((resolve, reject) => {
        if (owner.stopped) {
            reject(new Error('Pipeline stopped'));
            return;
        }
        owner.rejectFrame = reject;
        const tick = (time) => {
            const box = owner.canvas.getBoundingClientRect();
            if (document.hidden || box.width <= 0 || box.height <= 0) {
                owner.raf = requestAnimationFrame(tick);
                return;
            }
            const inputs = owner.inputs;
            owner.inputs = [];
            owner.rejectFrame = null;
            resolve(
                JSON.stringify([
                    time,
                    box.width,
                    box.height,
                    window.devicePixelRatio || 1,
                    ...inputs,
                ]),
            );
        };
        owner.raf = requestAnimationFrame(tick);
    });
}
export function disposePipelineCanvas(owner) {
    if (owner.stopped) return;
    owner.stopped = true;
    normalCanvases.delete(owner.canvas.id);
    cancelAnimationFrame(owner.raf);
    owner.abort.abort();
    owner.canvas.remove();
    owner.rejectFrame?.(new Error('Pipeline stopped'));
    owner.rejectFrame = null;
    owner.inputs = [];
    owner.image = null;
    owner.context = null;
}
