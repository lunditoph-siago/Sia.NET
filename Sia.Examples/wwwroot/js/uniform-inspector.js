import { createImageControl, imageBudget } from './image-input.js';

export function createUniformInspector(containerId) {
    return new UniformInspector(containerId);
}
export function disposeUniformInspector(owner) { owner.dispose(); }
export function createUniformBinding(owner) { return new UniformBinding(owner); }
export function configureUniformBinding(binding, json) { binding.configure(JSON.parse(json)); }
export function activateUniformBinding(binding) { binding.activate(); }
export function disposeUniformBinding(binding) { binding.dispose(); }
export function readUniformEdits(binding) {
    if (!binding.edits.size) return '';
    const json = JSON.stringify(Object.fromEntries(binding.edits));
    binding.edits.clear();
    return json;
}
export function readUniformImageEdits(binding) {
    if (!binding.imageEdits.size) return '';
    binding.imageReads = binding.imageEdits;
    binding.imageEdits = new Map();
    return JSON.stringify([...binding.imageReads].map(([key, image]) => ({
        key, name: image?.name ?? null, width: image?.width ?? 0, height: image?.height ?? 0,
    })));
}
export function readUniformImagePixels(binding, key) {
    const pixels = binding.imageReads.get(key)?.pixels ?? new Uint8Array();
    binding.imageReads.delete(key);
    return pixels;
}

function valid(field, values) {
    const number = field.type === 'f32' ? Math.fround : value => value;
    return values.length === field.values.length && values.every(value =>
        Number.isFinite(value) && (field.min == null || number(value) >= number(field.min)) &&
        (field.max == null || number(value) <= number(field.max)) &&
        (field.type !== 'f32' || Math.abs(value) <= 3.4028234663852886e38) &&
        (field.type !== 'i32' || Number.isInteger(value) && value >= -2147483648 && value <= 2147483647) &&
        (field.type !== 'u32' || Number.isInteger(value) && value >= 0 && value <= 4294967295) &&
        (field.type !== 'bool' || value === 0 || value === 1));
}

function bounded(field, values) {
    return values.map(value => Math.min(field.max ?? Infinity, Math.max(field.min ?? -Infinity, value)));
}

const decimal = new Intl.NumberFormat('en-US', { useGrouping: false, maximumSignificantDigits: 21 });
function parseDecimal(value) {
    return /^[+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:e[+-]?\d+)?$/i.test(value) ? Number(value) : NaN;
}

export class UniformBinding {
    constructor(owner) {
        this.owner = owner;
        this.fields = [];
        this.edits = new Map();
        this.imageEdits = new Map();
        this.imageReads = new Map();
        this.changed = () => {};
    }
    configure(fields) {
        for (const field of this.fields) field.abort?.abort();
        this.edits.clear();
        this.imageEdits.clear();
        this.imageReads.clear();
        const previous = new Map(this.fields.map(field => [field.signature, field]));
        this.fields = fields.map(field => {
            const signature = `${field.key}/${field.type}/${field.values?.length ?? 0}`;
            const saved = this.owner.values.get(signature);
            if (field.type === 'image') {
                const active = this.owner.active?.fields.find(row => row.signature === signature);
                const image = active ? active.image : saved ? saved.image : previous.get(signature)?.image ?? null;
                const row = { ...field, signature, image, abort: null };
                this.imageEdits.set(row.key, row.image);
                return row;
            }
            if (!valid(field, field.values)) throw new Error(`Invalid uniform default: ${field.name}`);
            const restored = saved && valid(field, saved.values);
            const row = { ...field, signature, initial: previous.get(signature)?.initial ?? bounded(field, field.values), initialSource: field.source ?? 'constant',
                values: bounded(field, restored ? saved.values : field.values),
                source: field.sources ? saved?.source ?? field.source ?? 'constant' : 'constant' };
            this.edits.set(row.key, [...row.values]);
            return row;
        });
        if (this.owner.active === this) this.owner.render();
    }
    activate() {
        this.owner.active = this;
        for (const field of this.fields) this.remember(field);
        this.owner.render();
    }
    remember(field) {
        const values = this.owner.values;
        values.delete(field.signature);
        values.set(field.signature, field.type === 'image' ? { image: field.image } : { values: [...field.values], source: field.source });
        if (values.size > 256) values.delete(values.keys().next().value);
        let bytes = [...values.values()].reduce((total, saved) => total + (saved.image?.pixels.length ?? 0), 0);
        for (const [key, saved] of values) {
            if (bytes <= imageBudget) break;
            if (saved.image) { bytes -= saved.image.pixels.length; values.delete(key); }
        }
    }
    updateImage(field, image) {
        if (!this.fields.includes(field)) return;
        const bytes = this.fields.reduce((total, row) => total + (row !== field ? row.image?.pixels.length ?? 0 : 0), image?.pixels.length ?? 0);
        if (bytes > imageBudget) throw new Error('Selected images may use at most 32 MB of pixels per session.');
        field.image = image;
        this.imageEdits.set(field.key, image);
        if (this.owner.active === this) this.remember(field);
        this.changed();
    }
    update(field, values, source = field.source) {
        if (!valid(field, values)) return false;
        field.values = bounded(field, values);
        field.source = source;
        this.edits.set(field.key, [...field.values]);
        if (this.owner.active === this) this.remember(field);
        this.changed();
        return true;
    }
    sample(field, time, width, height) {
        const raw = field.source === 'time' ? time : field.source === 'width' ? width :
            field.source === 'height' ? height : field.values[0];
        const bounded = Math.min(field.max ?? Infinity, Math.max(field.min ?? -Infinity, raw));
        const value = field.type === 'f32' ? bounded : Math.trunc(bounded);
        return valid(field, [value]) ? value : field.values[0];
    }
    dispose() {
        for (const field of this.fields) field.abort?.abort();
        this.fields = [];
        if (this.owner.active === this) {
            this.owner.active = null;
            this.owner.render();
        }
        this.changed = () => {};
        this.edits.clear();
        this.imageEdits.clear();
        this.imageReads.clear();
    }
}

class UniformInspector {
    constructor(containerId) {
        this.values = new Map();
        this.active = null;
        this.containerId = containerId;
        this.abort = new AbortController();
        this.resize = new ResizeObserver(() => this.place());
        this.button = document.createElement('button');
        this.button.type = 'button';
        this.button.className = 'icon-btn';
        this.button.setAttribute('aria-label', 'Uniforms');
        this.button.setAttribute('aria-haspopup', 'dialog');
        const icon = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
        icon.setAttribute('viewBox', '0 0 24 24');
        icon.setAttribute('width', '20');
        icon.setAttribute('height', '20');
        icon.setAttribute('fill', 'none');
        icon.setAttribute('stroke', 'currentColor');
        icon.setAttribute('stroke-width', '1.4');
        icon.setAttribute('stroke-linecap', 'round');
        icon.setAttribute('aria-hidden', 'true');
        const path = document.createElementNS('http://www.w3.org/2000/svg', 'path');
        path.setAttribute('d', 'M4 7h6m4 0h6M4 17h10m4 0h2M10 4v6m4 4v6');
        icon.append(path);
        this.button.append(icon);
        this.panel = document.createElement('section');
        this.panel.className = 'editor-uniform-popup';
        this.panel.id = containerId + '-panel';
        this.button.setAttribute('aria-controls', this.panel.id);
        this.button.setAttribute('popovertarget', this.panel.id);
        this.button.setAttribute('aria-expanded', 'false');
        this.panel.popover = 'auto';
        this.panel.setAttribute('role', 'dialog');
        this.panel.setAttribute('aria-label', 'Uniform inspector');
        const header = document.createElement('header');
        const title = document.createElement('strong');
        title.textContent = 'Uniforms';
        const reset = document.createElement('button');
        reset.type = 'button';
        reset.className = 'btn';
        reset.textContent = 'Reset';
        reset.addEventListener('click', () => {
            for (const field of this.active?.fields ?? []) {
                if (field.type === 'image') {
                    field.abort?.abort();
                    field.abort = null;
                    this.active.updateImage(field, null);
                }
                else this.active.update(field, field.initial, field.initialSource);
            }
            this.render();
        });
        const close = document.createElement('button');
        close.type = 'button';
        close.className = 'icon-btn';
        close.textContent = '×';
        close.setAttribute('aria-label', 'Close uniforms');
        close.addEventListener('click', () => { this.panel.hidePopover(); this.button.focus(); });
        header.append(title, reset, close);
        this.body = document.createElement('div');
        this.body.className = 'editor-uniform-fields';
        this.panel.append(header, this.body);
        document.body.append(this.panel);
        const sync = () => {
            this.button.setAttribute('aria-expanded', String(this.panel.matches(':popover-open')));
            this.place();
        };
        this.panel.addEventListener('toggle', sync);
        const place = () => this.place();
        window.addEventListener('resize', place, { signal: this.abort.signal });
        window.addEventListener('scroll', place, { capture: true, signal: this.abort.signal });
        window.visualViewport?.addEventListener('resize', place, { signal: this.abort.signal });
        window.visualViewport?.addEventListener('scroll', place, { signal: this.abort.signal });
        this.render();
    }
    place() {
        const root = this.surface;
        if (!root?.isConnected) {
            if (root) delete root.dataset.uniformPlacement;
            this.panel.hidePopover();
            return;
        }
        if (!this.panel.matches(':popover-open')) { delete root.dataset.uniformPlacement; return; }
        const viewport = window.visualViewport;
        const left = (viewport?.offsetLeft ?? 0) + 8, top = (viewport?.offsetTop ?? 0) + 8;
        const right = left + (viewport?.width ?? window.innerWidth) - 16;
        const bottom = top + (viewport?.height ?? window.innerHeight) - 16;
        const bounds = root.getBoundingClientRect();
        const header = this.button.closest('.editor-render-tools')?.getBoundingClientRect();
        const upperEdge = Math.min(bounds.top, header?.top ?? bounds.top);
        const above = Math.max(0, Math.min(bottom, upperEdge - 8) - top);
        const below = Math.max(0, bottom - Math.max(top, bounds.bottom + 8));
        const placement = bounds.width >= 640 && right - left >= 640 && bounds.left >= left - 8 && bounds.right <= right + 8 ? 'rail'
            : above >= 160 ? 'above' : below >= 160 ? 'below' : 'stack';
        root.dataset.uniformPlacement = placement;
        const space = document.getElementById('editor-uniform-space')?.getBoundingClientRect();
        const reserved = placement === 'rail' || placement === 'stack';
        if (reserved && (!space?.width || !space.height)) { this.panel.hidePopover(); return; }
        const width = Math.min(reserved ? space.width : Math.min(400, bounds.width), right - left);
        const y = reserved ? Math.max(top, space.top) : placement === 'above' ? top : Math.max(top, bounds.bottom + 8);
        const height = reserved ? Math.min(space.bottom, bottom) - y : Math.min(320, placement === 'above' ? above : below);
        if (width <= 0 || height <= 0) { this.panel.hidePopover(); return; }
        this.panel.style.width = `${width}px`;
        this.panel.style.maxHeight = `${height}px`;
        this.panel.style.left = `${Math.max(left, Math.min(right - width, reserved ? space.left : bounds.right - width))}px`;
        this.panel.style.top = `${placement === 'above' ? Math.min(bottom, upperEdge - 8) - Math.min(height, this.panel.offsetHeight) : y}px`;
    }
    render() {
        const container = document.getElementById(this.containerId);
        if (container && this.button.parentElement !== container) container.append(this.button);
        this.resize.disconnect();
        this.surface = document.getElementById(container?.dataset.renderSurface) ?? this.surface;
        if (this.surface) this.resize.observe(this.surface);
        this.resize.observe(this.panel);
        this.body.replaceChildren();
        const fields = this.active?.fields ?? [];
        this.button.disabled = fields.length === 0;
        this.button.title = fields.length ? 'Edit uniform parameters' : 'No editable uniform parameters';
        if (!fields.length) this.panel.hidePopover();
        const groups = new Map();
        for (const field of fields) {
            const separator = field.name.indexOf('.');
            const name = separator < 0 ? '' : field.name.slice(0, separator);
            let group = groups.get(name);
            if (!group) {
                group = document.createElement('fieldset');
                if (name) {
                    const legend = document.createElement('legend');
                    legend.textContent = name;
                    group.append(legend);
                }
                groups.set(name, group);
                this.body.append(group);
            }
            group.append(this.control(field, field.name.slice(separator + 1)));
        }
        this.place();
    }
    control(field, name) {
        if (field.type === 'image') return createImageControl(this.active, field, name);
        const row = document.createElement('div');
        row.className = 'editor-uniform-field';
        row.setAttribute('role', 'group');
        row.setAttribute('aria-label', field.name);
        const header = document.createElement(field.type === 'bool' ? 'label' : 'div');
        header.className = 'editor-uniform-label';
        const title = document.createElement('span');
        title.textContent = name;
        header.append(title);
        const controls = document.createElement('div');
        controls.className = 'editor-uniform-values';
        row.append(header, controls);
        const inputs = [];
        const error = document.createElement('span');
        error.className = 'editor-uniform-error';
        error.setAttribute('role', 'status');
        let picker, slider, source;
        const sync = (preserveInputs = false) => {
            inputs.forEach((input, index) => {
                if (!preserveInputs) {
                    if (field.type === 'bool') input.checked = field.values[index] !== 0;
                    else input.value = decimal.format(field.values[index]);
                    input.setAttribute('aria-invalid', 'false');
                }
                input.disabled = field.source !== 'constant';
                if (field.type !== 'bool') input.setAttribute('aria-valuenow', String(field.values[index]));
            });
            if (picker) {
                picker.value = '#' + field.values.slice(0, 3).map(value =>
                    Math.round(Math.min(1, Math.max(0, value)) * 255).toString(16).padStart(2, '0')).join('');
                picker.setAttribute('aria-invalid', 'false');
            }
            if (slider) {
                slider.value = String(field.values[0]);
                slider.disabled = field.source !== 'constant';
                slider.setAttribute('aria-invalid', 'false');
            }
            if (source) source.value = field.source;
        };
        const change = (values, control, mode = field.source) => {
            const ok = this.active.update(field, values, mode);
            control.setAttribute('aria-invalid', String(!ok));
            if (ok) sync(inputs.includes(control));
            const invalid = [...inputs, picker, slider, source].some(input => input?.getAttribute('aria-invalid') === 'true');
            error.textContent = invalid ? `Enter a valid ${field.type} value${field.min != null ? ` in [${field.min}, ${field.max}]` : ''}.` : '';
            return ok;
        };
        if (field.color) {
            picker = document.createElement('input');
            picker.type = 'color';
            picker.setAttribute('aria-label', `${field.name} color`);
            picker.addEventListener('input', () => change([
                ...[1, 3, 5].map(offset => parseInt(picker.value.slice(offset, offset + 2), 16) / 255),
                ...field.values.slice(3),
            ], picker));
            header.append(picker);
        }
        if (field.min != null && field.max != null && field.values.length === 1) {
            slider = document.createElement('input');
            slider.type = 'range';
            slider.min = field.min;
            slider.max = field.max;
            slider.step = field.type === 'f32' ? 'any' : '1';
            slider.setAttribute('aria-label', `${field.name} range`);
            slider.addEventListener('input', () => change([Number(slider.value)], slider));
            controls.append(slider);
        }
        if (field.sources) {
            source = document.createElement('select');
            source.setAttribute('aria-label', `${field.name} source`);
            for (const [value, label] of [['constant', 'Value'], ['time', 'Time (s)'], ['width', 'Canvas width'], ['height', 'Canvas height']]) {
                const option = document.createElement('option');
                option.value = value;
                option.textContent = label;
                source.append(option);
            }
            source.addEventListener('change', () => change(field.values, source, source.value));
            header.append(source);
        }
        field.values.forEach((_, index) => {
            const input = document.createElement('input');
            input.type = field.type === 'bool' ? 'checkbox' : 'text';
            if (field.type !== 'bool') {
                input.inputMode = field.type === 'f32' ? 'decimal' : 'numeric';
                input.autocomplete = 'off';
                input.spellcheck = false;
                input.setAttribute('role', 'spinbutton');
                if (field.min != null) input.setAttribute('aria-valuemin', field.min);
                if (field.max != null) input.setAttribute('aria-valuemax', field.max);
                input.addEventListener('blur', () => {
                    if (input.getAttribute('aria-invalid') !== 'true') input.value = decimal.format(field.values[index]);
                });
                input.addEventListener('keydown', event => {
                    if (event.key !== 'ArrowUp' && event.key !== 'ArrowDown') return;
                    event.preventDefault();
                    const values = [...field.values];
                    values[index] = Math.min(field.max ?? Infinity, Math.max(field.min ?? -Infinity,
                        field.values[index] + (event.key === 'ArrowUp' ? 1 : -1)));
                    if (change(values, input)) input.value = decimal.format(field.values[index]);
                });
            }
            input.setAttribute('aria-label', `${field.name}${field.values.length > 1 ? '.' + 'xyzw'[index] : ''} value`);
            input.addEventListener('input', () => {
                const values = [...field.values];
                values[index] = field.type === 'bool' ? Number(input.checked) : parseDecimal(input.value);
                change(values, input);
            });
            inputs.push(input);
            if (field.type === 'bool') header.append(input);
            else if (field.values.length > 1) {
                const component = document.createElement('label');
                component.className = 'editor-uniform-component';
                const axis = document.createElement('span');
                axis.textContent = (field.color ? 'RGBA' : 'XYZW')[index];
                component.append(axis, input);
                controls.append(component);
            }
            else controls.append(input);
        });
        row.append(error);
        sync();
        return row;
    }
    dispose() {
        const root = this.surface;
        if (root) delete root.dataset.uniformPlacement;
        this.active?.dispose();
        this.abort.abort();
        this.resize.disconnect();
        this.panel.remove();
        this.button.remove();
        this.active = null;
        this.values.clear();
    }
}
