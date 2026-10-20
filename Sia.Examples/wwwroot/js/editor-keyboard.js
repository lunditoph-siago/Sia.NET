export const initialObservation = () => ({
    status: 'unknown', baseline: null, width: null, orientation: null, started: false,
});

export function inferKeyboard(previous, sample) {
    if (sample.hidden) return previous;
    if (!sample.hasViewport || Math.abs(sample.scale - 1) > 0.05) return { ...previous, status: 'unknown' };
    const baseline = { visual: sample.visualHeight, layout: sample.layoutHeight };
    const changed = previous.width !== null && (Math.abs(sample.layoutWidth - previous.width) > 24
        || sample.orientation !== previous.orientation);
    const state = { ...previous, started: true, width: sample.layoutWidth, orientation: sample.orientation };
    if (changed) {
        const closed = previous.status === 'closed' && !sample.focused;
        return { ...state, baseline: closed ? baseline : null, status: closed ? 'closed' : 'unknown' };
    }
    if (!previous.baseline) {
        return !previous.started && !sample.focused
            ? { ...state, baseline, status: 'closed' }
            : { ...state, status: 'unknown' };
    }
    const loss = Math.max(0, previous.baseline.visual - sample.visualHeight, previous.baseline.layout - sample.layoutHeight);
    const openThreshold = Math.max(90, Math.min(220, previous.baseline.visual * 0.2));
    const closeThreshold = Math.max(45, Math.min(90, openThreshold * 0.55));
    if (sample.nativeOverlay) return { ...state, status: 'unknown' };
    if (sample.focused && loss >= openThreshold) return { ...state, status: 'open' };
    if (!sample.focused && loss >= openThreshold && previous.status !== 'open') {
        return { ...state, baseline: null, status: 'unknown' };
    }
    return loss <= closeThreshold
        ? { ...state, status: 'closed', baseline: sample.focused ? previous.baseline : baseline }
        : state;
}

export function hasTextFocus(element) {
    if (!element || element.closest?.('[inert]')) return false;
    if (element.isContentEditable) return true;
    if (element.disabled || element.readOnly || element.inputMode === 'none') return false;
    return element.tagName === 'TEXTAREA'
        || element.tagName === 'INPUT' && /^(text|search|tel|url|email|password|number)$/.test(element.type);
}

export function observeSoftKeyboard(callback) {
    const abort = new AbortController();
    const viewport = window.visualViewport;
    const keyboard = navigator.virtualKeyboard;
    let previousOverlay, nativeEnabled = false;
    if (keyboard) {
        try {
            previousOverlay = keyboard.overlaysContent;
            keyboard.overlaysContent = true;
            nativeEnabled = keyboard.overlaysContent === true;
        } catch {}
    }
    let state = initialObservation(), frame = 0, quiet = 0, deadline = 0, disposed = false;
    const read = () => ({
        hasViewport: !!viewport, visualHeight: viewport?.height ?? window.innerHeight,
        offsetTop: viewport?.offsetTop ?? 0, scale: viewport?.scale ?? 1,
        layoutWidth: document.documentElement.clientWidth, layoutHeight: document.documentElement.clientHeight,
        orientation: screen.orientation?.type ?? (screen.width > screen.height ? 'landscape' : 'portrait'),
        focused: hasTextFocus(document.activeElement), hidden: document.visibilityState === 'hidden',
        nativeOverlay: nativeEnabled, rectangle: nativeEnabled ? keyboard.boundingRect : null,
    });
    const publish = (sample, pending) => {
        if (disposed || sample.hidden) return;
        const nativeStatus = sample.rectangle?.width > 0 && sample.rectangle.height > 0 ? 'open' : 'closed';
        callback({ ...sample, status: nativeEnabled ? nativeStatus : state.status,
            source: nativeEnabled ? 'native' : 'viewport', pending });
    };
    const commit = () => {
        clearTimeout(quiet); clearTimeout(deadline); quiet = deadline = 0;
        if (disposed) return;
        const sample = read();
        state = inferKeyboard(state, sample);
        publish(sample, false);
    };
    const update = () => {
        frame = 0;
        const sample = read();
        if (sample.hidden) {
            clearTimeout(quiet); clearTimeout(deadline); quiet = deadline = 0;
            return;
        }
        if (nativeEnabled) { commit(); return; }
        publish(sample, true);
        clearTimeout(quiet);
        quiet = setTimeout(commit, 120);
        if (!deadline) deadline = setTimeout(commit, 350);
    };
    const schedule = () => {
        if (!disposed && !frame) frame = requestAnimationFrame(update);
    };
    window.addEventListener('resize', schedule, { signal: abort.signal });
    viewport?.addEventListener('resize', schedule, { signal: abort.signal });
    viewport?.addEventListener('scroll', schedule, { signal: abort.signal });
    window.addEventListener('pageshow', schedule, { signal: abort.signal });
    screen.orientation?.addEventListener('change', schedule, { signal: abort.signal });
    for (const name of ['focusin', 'focusout', 'visibilitychange']) document.addEventListener(name, schedule, { signal: abort.signal });
    if (nativeEnabled) keyboard.addEventListener('geometrychange', schedule, { signal: abort.signal });
    commit();
    return {
        refresh: schedule,
        reset() { if (!disposed) { state = initialObservation(); commit(); } },
        dispose() {
            if (disposed) return;
            disposed = true; abort.abort(); cancelAnimationFrame(frame);
            clearTimeout(quiet); clearTimeout(deadline);
            if (nativeEnabled) {
                try { keyboard.overlaysContent = previousOverlay; } catch {}
            }
        },
    };
}
