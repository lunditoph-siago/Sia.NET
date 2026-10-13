// Emscripten owns the Worker; the editor owns and transfers only its canvas.
addToLibrary({
    $siaEditorCanvasBridge__deps: ['$GL', '$PThread'],
    $siaEditorCanvasBridge: { released: new Set(), initialized: false },
    $siaEditorCanvasBridge__postset: `
        if (!ENVIRONMENT_IS_PTHREAD) {
            Module['siaEditorTransferCanvas'] = (canvas, thread, signal) => {
                const worker = PThread.pthreads[thread];
                if (!canvas.transferControlToOffscreen || !worker)
                    throw new Error('OffscreenCanvas or the render Worker is unavailable');
                const channel = new MessageChannel();
                return new Promise((resolve, reject) => {
                    const cleanup = () => {
                        clearTimeout(timeout);
                        channel.port1.close();
                        signal.removeEventListener('abort', aborted);
                    };
                    const received = event => {
                        cleanup(); resolve();
                    };
                    const aborted = () => { cleanup(); reject(new Error('Canvas transfer cancelled')); };
                    const timeout = setTimeout(() => { cleanup(); reject(new Error('Canvas transfer timed out')); }, 5000);
                    channel.port1.onmessage = received;
                    signal.addEventListener('abort', aborted, { once: true });
                    try {
                        const offscreen = canvas.transferControlToOffscreen();
                        worker.postMessage({ cmd: 'sia-editor-canvas', id: canvas.id, canvas: offscreen, port: channel.port2 }, [offscreen, channel.port2]);
                    } catch (error) { cleanup(); reject(error); }
                });
            };
        }
    `,
    siaEditorInitCanvasWorker__sig: 'v',
    siaEditorInitCanvasWorker__deps: ['$siaEditorCanvasBridge', '$GL'],
    siaEditorInitCanvasWorker: () => {
        if (!ENVIRONMENT_IS_PTHREAD || siaEditorCanvasBridge.initialized) return;
        siaEditorCanvasBridge.initialized = true;
        self.addEventListener(
            'message',
            (event) => {
                if (event.data.cmd !== 'sia-editor-canvas') return;
                event.stopImmediatePropagation();
                const { id, canvas, port } = event.data;
                if (siaEditorCanvasBridge.released.delete(id)) {
                    port.close();
                    return;
                }
                GL.offscreenCanvases[id] = { id, offscreenCanvas: canvas };
                port.postMessage(null);
                port.close();
            },
            true,
        );
    },
    siaEditorResizeCanvas__sig: 'vpii',
    siaEditorResizeCanvas__deps: ['$GL', '$UTF8ToString'],
    siaEditorResizeCanvas: (selector, width, height) => {
        const canvas = GL.offscreenCanvases[UTF8ToString(selector).slice(1)].offscreenCanvas;
        if (canvas.width !== width) canvas.width = width;
        if (canvas.height !== height) canvas.height = height;
    },
    siaEditorObserveDevice__sig: 'vpp',
    siaEditorObserveDevice__deps: ['$GL', '$UTF8ToString', '$WebGPU'],
    siaEditorObserveDevice: (selector, device) => {
        const record = GL.offscreenCanvases[UTF8ToString(selector).slice(1)];
        record.device = WebGPU.getJsObject(device);
        record.error = '';
        record.validated = false;
        record.device.pushErrorScope('validation');
        record.errorListener = (event) => {
            record.error ||= event.error.message;
        };
        record.device.addEventListener('uncapturederror', record.errorListener);
        record.device.lost.then((info) => {
            if (!record.closed) record.error = 'Device lost: ' + info.message;
        });
    },
    siaEditorValidateFrame__sig: 'vp',
    siaEditorValidateFrame__deps: ['$GL', '$UTF8ToString'],
    siaEditorValidateFrame: (selector) => {
        const record = GL.offscreenCanvases[UTF8ToString(selector).slice(1)];
        record.device
            .popErrorScope()
            .then((error) => {
                if (record.closed) return;
                if (error) record.error ||= error.message;
                record.validated = true;
            })
            .catch((error) => {
                if (!record.closed) record.error ||= error.message;
            });
    },
    siaEditorFrameValidated__sig: 'ip',
    siaEditorFrameValidated__deps: ['$GL', '$UTF8ToString'],
    siaEditorFrameValidated: (selector) =>
        GL.offscreenCanvases[UTF8ToString(selector).slice(1)]?.validated ? 1 : 0,
    siaEditorDeviceError__sig: 'pp',
    siaEditorDeviceError__deps: ['$GL', '$UTF8ToString', '$stringToNewUTF8'],
    siaEditorDeviceError: (selector) => {
        const error = GL.offscreenCanvases[UTF8ToString(selector).slice(1)]?.error;
        return error ? stringToNewUTF8(error) : 0;
    },
    siaEditorReleaseCanvas__sig: 'vp',
    siaEditorReleaseCanvas__deps: ['$GL', '$UTF8ToString', '$siaEditorCanvasBridge'],
    siaEditorReleaseCanvas: (selector) => {
        const id = UTF8ToString(selector).slice(1);
        const record = GL.offscreenCanvases[id];
        if (record) {
            record.closed = true;
            if (record.device) {
                record.device.removeEventListener('uncapturederror', record.errorListener);
                record.device.destroy();
            }
            delete GL.offscreenCanvases[id];
        } else siaEditorCanvasBridge.released.add(id);
    },
});
