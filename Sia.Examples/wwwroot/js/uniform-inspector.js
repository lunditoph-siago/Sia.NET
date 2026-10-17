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

export class UniformBinding {
    constructor(owner) {
        this.owner = owner;
        this.fields = [];
        this.edits = new Map();
        this.changed = () => {};
    }
    configure(fields) {
        const previous = new Map(this.fields.map(field => [field.signature, field]));
        this.fields = fields.map(field => {
            const signature = `${field.key}/${field.type}/${field.values.length}`;
            const saved = this.owner.values.get(signature);
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
        values.set(field.signature, { values: [...field.values], source: field.source });
        if (values.size > 256) values.delete(values.keys().next().value);
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
        if (this.owner.active === this) {
            this.owner.active = null;
            this.owner.render();
        }
        this.changed = () => {};
        this.edits.clear();
    }
}

class UniformInspector {
    constructor(containerId) {
        this.values = new Map();
        this.active = null;
        this.containerId = containerId;
        this.abort = new AbortController();
        this.button = document.createElement('button');
        this.button.type = 'button';
        this.button.className = 'btn';
        this.button.textContent = 'Uniforms';
        this.panel = document.createElement('section');
        this.panel.className = 'editor-uniform-popup';
        this.panel.id = containerId + '-panel';
        this.button.setAttribute('aria-controls', this.panel.id);
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
            for (const field of this.active?.fields ?? [])
                this.active.update(field, field.initial, field.initialSource);
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
        this.button.addEventListener('click', () => { this.panel.togglePopover(); this.place(); });
        this.panel.addEventListener('toggle', () => this.button.setAttribute('aria-expanded', String(this.panel.matches(':popover-open'))));
        const place = () => this.place();
        window.addEventListener('resize', place, { signal: this.abort.signal });
        window.addEventListener('scroll', place, { capture: true, signal: this.abort.signal });
        this.render();
    }
    place() {
        if (!this.panel.matches(':popover-open')) return;
        const anchor = this.button.getBoundingClientRect();
        const width = this.panel.offsetWidth, height = this.panel.offsetHeight;
        this.panel.style.left = `${Math.max(8, Math.min(anchor.right - width, innerWidth - width - 8))}px`;
        this.panel.style.top = `${Math.max(8, Math.min(anchor.bottom + 6, innerHeight - height - 8))}px`;
    }
    render() {
        const container = document.getElementById(this.containerId);
        if (container && this.button.parentElement !== container) container.append(this.button);
        this.body.replaceChildren();
        const fields = this.active?.fields ?? [];
        this.button.disabled = fields.length === 0;
        if (!fields.length) this.panel.hidePopover();
        for (const field of fields) this.body.append(this.control(field));
        this.place();
    }
    control(field) {
        const row = document.createElement('fieldset');
        const legend = document.createElement('legend');
        legend.textContent = field.name;
        row.append(legend);
        const inputs = [];
        const error = document.createElement('span');
        error.className = 'editor-uniform-error';
        error.setAttribute('role', 'status');
        let picker, slider, source;
        const sync = () => {
            inputs.forEach((input, index) => {
                if (field.type === 'bool') input.checked = field.values[index] !== 0;
                else input.value = String(field.values[index]);
                input.disabled = field.source !== 'constant';
            });
            if (picker) picker.value = '#' + field.values.slice(0, 3).map(value =>
                Math.round(Math.min(1, Math.max(0, value)) * 255).toString(16).padStart(2, '0')).join('');
            if (slider) slider.value = String(field.values[0]);
            if (source) source.value = field.source;
        };
        const change = values => {
            const ok = this.active.update(field, values);
            error.textContent = ok ? '' : `Enter a valid ${field.type} value${field.min != null ? ` in [${field.min}, ${field.max}]` : ''}.`;
            inputs.forEach(input => input.setAttribute('aria-invalid', String(!ok)));
            if (ok) sync();
        };
        if (field.color) {
            picker = document.createElement('input');
            picker.type = 'color';
            picker.setAttribute('aria-label', `${field.name} color`);
            picker.addEventListener('input', () => change([
                ...[1, 3, 5].map(offset => parseInt(picker.value.slice(offset, offset + 2), 16) / 255),
                ...field.values.slice(3),
            ]));
            row.append(picker);
        }
        if (field.min != null && field.max != null && field.values.length === 1) {
            slider = document.createElement('input');
            slider.type = 'range';
            slider.min = field.min;
            slider.max = field.max;
            slider.step = field.type === 'f32' ? 'any' : '1';
            slider.setAttribute('aria-label', `${field.name} range`);
            slider.addEventListener('input', () => change([Number(slider.value)]));
            row.append(slider);
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
            source.addEventListener('change', () => { this.active.update(field, field.values, source.value); sync(); });
            row.append(source);
        }
        field.values.forEach((_, index) => {
            const input = document.createElement('input');
            input.type = field.type === 'bool' ? 'checkbox' : 'number';
            input.step = field.type === 'f32' ? 'any' : '1';
            if (field.min != null) input.min = field.min;
            if (field.max != null) input.max = field.max;
            input.setAttribute('aria-label', `${field.name}${field.values.length > 1 ? '.' + 'xyzw'[index] : ''} value`);
            input.addEventListener('input', () => change(inputs.map(control => field.type === 'bool' ? Number(control.checked) :
                control.value === '' ? NaN : Number(control.value))));
            inputs.push(input);
            row.append(input);
        });
        row.append(error);
        sync();
        return row;
    }
    dispose() {
        this.abort.abort();
        this.panel.remove();
        this.button.remove();
        this.active = null;
        this.values.clear();
    }
}
