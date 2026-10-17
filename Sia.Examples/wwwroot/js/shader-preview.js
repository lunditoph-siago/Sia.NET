import { UniformBinding } from './uniform-inspector.js';

const fullscreenVertex = `
@vertex fn fullscreen(@builtin(vertex_index) index: u32) -> @builtin(position) vec4<f32> {
    let x = f32((index << 1u) & 2u);
    let y = f32(index & 2u);
    return vec4<f32>(x * 2.0 - 1.0, y * 2.0 - 1.0, 0.0, 1.0);
}`;

export function createShaderPreview(containerId, statusId, inspector) {
    return new ShaderPreview(
        document.getElementById(containerId),
        document.getElementById(statusId),
        inspector,
    );
}
export function setShaderPreviewRevision(owner, revision) {
    owner.invalidate(revision);
}
export function showShaderPreview(owner, wgsl, entry, layoutJson, revision, route) {
    return owner.show(wgsl, entry, JSON.parse(layoutJson), revision, route);
}
export function disposeShaderPreview(owner) {
    owner.dispose();
}
export function downloadShaderBinary(name, base64) {
    const bytes = Uint8Array.from(atob(base64), (char) => char.charCodeAt(0));
    const url = URL.createObjectURL(new Blob([bytes], { type: 'application/octet-stream' }));
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = name;
    anchor.click();
    setTimeout(() => URL.revokeObjectURL(url), 0);
}

class ShaderPreview {
    constructor(container, status, inspector) {
        this.container = container;
        this.status = status;
        this.canvas = document.createElement('canvas');
        this.canvas.className = 'editor-shader-canvas';
        this.canvas.setAttribute('aria-label', 'Shader preview');
        this.inspector = inspector;
        this.pause = document.createElement('button');
        this.pause.className = 'btn';
        this.pause.textContent = 'Pause';
        this.pause.type = 'button';
        this.pause.addEventListener('click', () => this.togglePause());
        container.append(this.canvas, this.pause);
        this.disposed = false;
        this.revision = 0;
        this.visible = false;
        this.paused = false;
        this.time = 0;
        this.started = performance.now();
        this.frame = 0;
        this.drawError = false;
        this.parameters = [];
        this.resize = new ResizeObserver(() => this.refresh());
        this.resize.observe(this.canvas);
        this.visibility = new IntersectionObserver((entries) => {
            this.visible = entries[0].isIntersecting;
            this.refresh();
        });
        this.visibility.observe(this.canvas);
        this.onVisibility = () => this.refresh();
        document.addEventListener('visibilitychange', this.onVisibility);
        this.onError = (event) => {
            this.stop();
            this.status.textContent = `GPU error: ${event.error.message}`;
        };
    }

    invalidate(revision) {
        this.revision = revision;
        this.status.textContent = this.pipeline
            ? 'Previous preview · compile the current source to update'
            : 'Compile and preview a fragment shader';
    }

    async ensureDevice() {
        if (this.device) return this.device;
        if (this.acquiring) return this.acquiring;
        this.acquiring = (async () => {
            if (!navigator.gpu)
                throw new Error(
                    'WebGPU is unavailable. Use a supported browser in a secure context.',
                );
            const adapter = await navigator.gpu.requestAdapter();
            if (!adapter) throw new Error('No WebGPU adapter is available.');
            const device = await adapter.requestDevice();
            if (this.disposed) {
                device.destroy();
                throw new Error('Preview was closed.');
            }
            this.context = this.canvas.getContext('webgpu');
            if (!this.context) {
                device.destroy();
                throw new Error('WebGPU canvas context is unavailable.');
            }
            this.device = device;
            this.configured = false;
            this.format = navigator.gpu.getPreferredCanvasFormat();
            device.addEventListener('uncapturederror', this.onError);
            device.lost.then((info) => {
                if (this.disposed || this.device !== device) return;
                this.stop();
                this.buffer?.destroy();
                this.buffer = null;
                this.pipeline = null;
                this.device = null;
                this.context.unconfigure();
                this.configured = false;
                this.status.textContent = `Device lost: ${info.message || info.reason}. Preview again to retry.`;
            });
            return device;
        })();
        try {
            return await this.acquiring;
        } finally {
            this.acquiring = null;
        }
    }

    async show(wgsl, entry, layout, revision, route) {
        const started = performance.now();
        const device = await this.ensureDevice();
        if (this.disposed || revision !== this.revision) return -1;
        if (
            layout.bufferSize > device.limits.maxUniformBufferBindingSize ||
            (layout.binding !== null && layout.binding >= device.limits.maxBindingsPerBindGroup)
        ) {
            throw new Error('Preview parameters exceed the adapter limits.');
        }
        let buffer = null;
        let binding = null;
        let scoped = true;
        device.pushErrorScope('validation');
        try {
            const fragment = device.createShaderModule({ code: wgsl, label: entry });
            const info = await fragment.getCompilationInfo();
            const errors = info.messages.filter((message) => message.type === 'error');
            if (errors.length)
                throw new Error(
                    errors
                        .map(
                            (message) =>
                                `WGSL(${message.lineNum},${message.linePos}): ${message.message}`,
                        )
                        .join('\n'),
                );
            if (this.disposed || revision !== this.revision) return -1;
            const vertex = device.createShaderModule({
                code: fullscreenVertex,
                label: 'Fullscreen host',
            });
            const groupLayout =
                layout.binding === null
                    ? null
                    : device.createBindGroupLayout({
                          entries: [
                              {
                                  binding: layout.binding,
                                  visibility: GPUShaderStage.FRAGMENT,
                                  buffer: { type: 'uniform', minBindingSize: layout.bufferSize },
                              },
                          ],
                      });
            const pipeline = await device.createRenderPipelineAsync({
                layout: device.createPipelineLayout({
                    bindGroupLayouts: groupLayout ? [groupLayout] : [],
                }),
                vertex: { module: vertex, entryPoint: 'fullscreen' },
                fragment: {
                    module: fragment,
                    entryPoint: entry,
                    targets: [{ format: this.format }],
                },
                primitive: { topology: 'triangle-list' },
            });
            let bindGroup = null;
            if (groupLayout) {
                buffer = device.createBuffer({
                    size: layout.bufferSize,
                    usage: GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST,
                });
                bindGroup = device.createBindGroup({
                    layout: groupLayout,
                    entries: [{ binding: layout.binding, resource: { buffer } }],
                });
            }
            const validation = await device.popErrorScope();
            scoped = false;
            if (validation) throw new Error(validation.message);
            if (this.disposed || revision !== this.revision || this.device !== device) return -1;
            binding = new UniformBinding(this.inspector);
            binding.configure(layout.parameters.map(parameter => {
                const min = parameter.type === 'f32' ? parameter.min : Math.ceil(parameter.min ?? -2147483648);
                const max = parameter.type === 'f32' ? parameter.max : Math.floor(parameter.max ?? 4294967295);
                return { ...parameter, key: `${entry}/${parameter.name}`,
                    values: [Math.min(max ?? Infinity, Math.max(min ?? -Infinity, 0))], sources: true,
                    source: ['time', 'width', 'height'].includes(parameter.name) ? parameter.name : 'constant' };
            }));
            // Only a complete, current pipeline can take ownership of the live preview.
            this.stop();
            const oldBuffer = this.buffer;
            this.pipeline = pipeline;
            this.buffer = buffer;
            buffer = null;
            this.bindGroup = bindGroup;
            this.uniformBytes = new ArrayBuffer(layout.bufferSize);
            this.binding?.dispose();
            this.binding = binding;
            binding = null;
            this.parameters = this.binding.fields;
            this.binding.changed = () => this.refresh();
            this.binding.activate();
            this.activeRevision = revision;
            this.time = 0;
            this.started = performance.now();
            oldBuffer?.destroy();
            const milliseconds = performance.now() - started;
            this.pipelineStatus = `${route} · ${entry} · GPU pipeline ${milliseconds.toFixed(1)} ms`;
            this.drawError = false;
            this.status.textContent = this.pipelineStatus;
            this.refresh();
            return milliseconds;
        } finally {
            if (scoped) await device.popErrorScope();
            buffer?.destroy();
            binding?.dispose();
        }
    }

    stop() {
        cancelAnimationFrame(this.frame);
        this.frame = 0;
    }

    togglePause() {
        if (!this.paused) this.time += (performance.now() - this.started) / 1000;
        this.started = performance.now();
        this.paused = !this.paused;
        this.pause.textContent = this.paused ? 'Resume' : 'Pause';
        this.refresh();
    }

    refresh() {
        this.stop();
        if (this.disposed || !this.device || !this.pipeline || !this.visible || document.hidden)
            return;
        const rect = this.canvas.getBoundingClientRect();
        if (rect.width <= 0 || rect.height <= 0) return;
        const maximum = this.device.limits.maxTextureDimension2D;
        const width = Math.max(1, Math.min(maximum, Math.round(rect.width * devicePixelRatio)));
        const height = Math.max(1, Math.min(maximum, Math.round(rect.height * devicePixelRatio)));
        if (width !== this.canvas.width || height !== this.canvas.height || !this.configured) {
            this.canvas.width = width;
            this.canvas.height = height;
            this.context.configure({
                device: this.device,
                format: this.format,
                alphaMode: 'opaque',
            });
            this.configured = true;
        }
        this.draw(performance.now());
    }

    draw(now) {
        if (this.disposed || !this.device || !this.pipeline) return;
        try {
            if (this.buffer) {
                const view = new DataView(this.uniformBytes);
                for (const parameter of this.parameters) {
                    const value =
                        this.binding.sample(parameter,
                            this.time + (this.paused ? 0 : (now - this.started) / 1000), this.canvas.width, this.canvas.height);
                    if (
                        !Number.isFinite(value) ||
                        (parameter.type === 'f32' && Math.abs(value) > 3.4028234663852886e38) ||
                        (parameter.type === 'u32' && (value < 0 || value > 4294967295)) ||
                        (parameter.type === 'i32' && (value < -2147483648 || value > 2147483647))
                    ) {
                        throw new Error(`Invalid ${parameter.type} value for ${parameter.name}`);
                    }
                    if (parameter.type === 'f32') view.setFloat32(parameter.offset, value, true);
                    else if (parameter.type === 'i32')
                        view.setInt32(parameter.offset, Math.trunc(value), true);
                    else view.setUint32(parameter.offset, Math.trunc(value), true);
                }
                this.device.queue.writeBuffer(this.buffer, 0, this.uniformBytes);
            }
            const encoder = this.device.createCommandEncoder();
            const pass = encoder.beginRenderPass({
                colorAttachments: [
                    {
                        view: this.context.getCurrentTexture().createView(),
                        clearValue: { r: 0, g: 0, b: 0, a: 1 },
                        loadOp: 'clear',
                        storeOp: 'store',
                    },
                ],
            });
            pass.setPipeline(this.pipeline);
            if (this.bindGroup) pass.setBindGroup(0, this.bindGroup);
            pass.draw(3);
            pass.end();
            this.device.queue.submit([encoder.finish()]);
            if (this.drawError) {
                this.drawError = false;
                this.status.textContent =
                    this.activeRevision === this.revision
                        ? this.pipelineStatus
                        : 'Previous preview · compile the current source to update';
            }
            if (
                !this.paused &&
                this.parameters.some((parameter) => parameter.source === 'time')
            ) {
                this.frame = requestAnimationFrame((time) => this.draw(time));
            }
        } catch (error) {
            this.stop();
            this.drawError = true;
            this.status.textContent = `Preview failed: ${error.message}`;
        }
    }

    dispose() {
        if (this.disposed) return;
        this.disposed = true;
        this.revision++;
        this.stop();
        this.resize.disconnect();
        this.visibility.disconnect();
        document.removeEventListener('visibilitychange', this.onVisibility);
        this.buffer?.destroy();
        this.binding?.dispose();
        if (this.device) {
            this.device.removeEventListener('uncapturederror', this.onError);
            this.context.unconfigure();
            this.device.destroy();
        }
        this.device = this.pipeline = this.buffer = this.bindGroup = null;
        this.container.replaceChildren();
    }
}
