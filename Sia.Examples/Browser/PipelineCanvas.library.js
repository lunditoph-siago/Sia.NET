// Emscripten owns the Worker; the editor owns and transfers only its canvas.
addToLibrary({
    $siaEditorCanvasBridge__deps: ['$GL', '$PThread'],
    $siaEditorCanvasBridge: { released: new Set(), devices: new Map(), ports: new Map(), initialized: false },
    $siaEditorCanvasBridge__postset: `
        if (!ENVIRONMENT_IS_PTHREAD) {
            Module['siaEditorReadbackCanvas'] = (canvas, thread, signal, present) => {
                const worker = PThread.pthreads[thread];
                if (!worker) throw new Error('Render Worker is unavailable');
                const channel = new MessageChannel();
                return new Promise((resolve, reject) => {
                    const cleanup = () => {
                        clearTimeout(timeout);
                        channel.port1.close();
                        signal.removeEventListener('abort', aborted);
                    };
                    const aborted = () => { cleanup(); reject(new Error('Canvas stopped')); };
                    const timeout = setTimeout(() => { cleanup(); reject(new Error('Canvas initialization timed out')); }, 5000);
                    channel.port1.onmessage = (event) => {
                        if (event.data.ready) { clearTimeout(timeout); resolve(); return; }
                        const { pixels, width, height } = event.data;
                        present('#' + canvas.id, pixels, width, height);
                    };
                    signal.addEventListener('abort', aborted, { once: true });
                    try {
                        worker.postMessage({ cmd: 'sia-editor-readback', id: canvas.id, port: channel.port2 }, [channel.port2]);
                    } catch (error) { cleanup(); reject(error); }
                });
            };
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
                    const aborted = () => { cleanup(); reject(new Error('Canvas transfer cancelled')); };
                    const timeout = setTimeout(() => { cleanup(); reject(new Error('Canvas transfer timed out')); }, 5000);
                    channel.port1.onmessage = () => { cleanup(); resolve(); };
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
                if (event.data.cmd !== 'sia-editor-canvas' && event.data.cmd !== 'sia-editor-readback') return;
                event.stopImmediatePropagation();
                const { id, canvas, port } = event.data;
                if (siaEditorCanvasBridge.released.delete(id)) {
                    port.close();
                    return;
                }
                if (event.data.cmd === 'sia-editor-readback') {
                    siaEditorCanvasBridge.ports.set(id, port);
                    port.postMessage({ ready: true });
                } else {
                    GL.offscreenCanvases[id] = { id, offscreenCanvas: canvas };
                    port.postMessage(null);
                    port.close();
                }
            },
            true,
        );
    },
    siaEditorObserveDevice__sig: 'vpp',
    siaEditorObserveDevice__deps: ['$siaEditorCanvasBridge', '$UTF8ToString', '$WebGPU'],
    siaEditorObserveDevice: (selector, device) => {
        const record = { device: WebGPU.getJsObject(device), error: '', validated: false, closed: false };
        siaEditorCanvasBridge.devices.set(UTF8ToString(selector).slice(1), record);
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
    siaEditorValidateFrame__deps: ['$siaEditorCanvasBridge', '$UTF8ToString'],
    siaEditorValidateFrame: (selector) => {
        const record = siaEditorCanvasBridge.devices.get(UTF8ToString(selector).slice(1));
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
    siaEditorFrameValidated__deps: ['$siaEditorCanvasBridge', '$UTF8ToString'],
    siaEditorFrameValidated: (selector) =>
        siaEditorCanvasBridge.devices.get(UTF8ToString(selector).slice(1))?.validated ? 1 : 0,
    siaEditorDeviceError__sig: 'pp',
    siaEditorDeviceError__deps: ['$siaEditorCanvasBridge', '$UTF8ToString', '$stringToNewUTF8'],
    siaEditorDeviceError: (selector) => {
        const error = siaEditorCanvasBridge.devices.get(UTF8ToString(selector).slice(1))?.error;
        return error ? stringToNewUTF8(error) : 0;
    },
    siaEditorReadPixels__sig: 'vppiii',
    siaEditorReadPixels__deps: ['$siaEditorCanvasBridge', '$UTF8ToString', '$WebGPU', '$runtimeKeepalivePush', '$runtimeKeepalivePop'],
    siaEditorReadPixels: (selector, buffer, width, height, stride) => {
        const id = UTF8ToString(selector).slice(1);
        const record = siaEditorCanvasBridge.devices.get(id);
        const source = WebGPU.getJsObject(buffer);
        record.pending = true;
        runtimeKeepalivePush();
        Promise.resolve().then(() => source.mapAsync(GPUMapMode.READ, 0, stride * height)).then(() => {
            try {
                if (record.closed) return;
                const mapped = new Uint8Array(source.getMappedRange(0, stride * height));
                const pixels = new Uint8Array(width * height * 4);
                for (let row = 0; row < height; row++)
                    pixels.set(mapped.subarray(row * stride, row * stride + width * 4), row * width * 4);
                // Match the opaque Surface used by the direct presentation path.
                for (let alpha = 3; alpha < pixels.length; alpha += 4) pixels[alpha] = 255;
                siaEditorCanvasBridge.ports.get(id).postMessage({ pixels, width, height }, [pixels.buffer]);
            } finally { source.unmap(); }
        }).catch((error) => {
            if (!record.closed) record.error ||= error.message;
        }).finally(() => { record.pending = false; runtimeKeepalivePop(); });
    },
    siaEditorPixelsPending__sig: 'ip',
    siaEditorPixelsPending__deps: ['$siaEditorCanvasBridge', '$UTF8ToString'],
    siaEditorPixelsPending: (selector) =>
        siaEditorCanvasBridge.devices.get(UTF8ToString(selector).slice(1))?.pending ? 1 : 0,
    siaEditorReleaseCanvas__sig: 'vpi',
    siaEditorReleaseCanvas__deps: ['$GL', '$UTF8ToString', '$siaEditorCanvasBridge'],
    siaEditorReleaseCanvas: (selector, offscreen) => {
        const id = UTF8ToString(selector).slice(1);
        const record = siaEditorCanvasBridge.devices.get(id);
        if (record) {
            record.closed = true;
            record.device.removeEventListener('uncapturederror', record.errorListener);
            siaEditorCanvasBridge.devices.delete(id);
        }
        const port = siaEditorCanvasBridge.ports.get(id);
        if (port) { port.close(); siaEditorCanvasBridge.ports.delete(id); }
        else if (!offscreen) siaEditorCanvasBridge.released.add(id);
        if (offscreen) {
            if (GL.offscreenCanvases[id]) delete GL.offscreenCanvases[id];
            else siaEditorCanvasBridge.released.add(id);
        }
    },
});
