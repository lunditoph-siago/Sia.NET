const events = [];
const waiters = [];
const barriers = new Map();
let activeBarrier;
const scheduledEvents = new Map();

export function emit(payload, coalesceKey) {
    const previous = events.at(-1);
    if (coalesceKey !== undefined && previous?.coalesceKey === coalesceKey) {
        previous.payload = payload;
    } else {
        events.push({ payload, coalesceKey });
    }
    flushEvents();
}

export function emitBarrier(payload, key) {
    if (barriers.has(key)) throw new Error('Duplicate event barrier.');
    barriers.set(key, { released: false });
    events.push({ payload, barrier: key });
    flushEvents();
}

export function resolveBarrier(key, followup) {
    const barrier = barriers.get(key);
    if (!barrier || barrier.released) return;
    barrier.released = true;
    barrier.followup = followup;
    if (activeBarrier === key) {
        activeBarrier = undefined;
        barriers.delete(key);
        events.unshift({ payload: followup });
        flushEvents();
    }
}

function takeEvent() {
    if (activeBarrier || !events.length) return undefined;
    const event = events.shift();
    if (event.barrier) {
        const barrier = barriers.get(event.barrier);
        if (barrier.released) {
            barriers.delete(event.barrier);
            events.unshift({ payload: barrier.followup });
        } else activeBarrier = event.barrier;
    }
    return event.payload;
}

function flushEvents() {
    while (waiters.length) {
        const payload = takeEvent();
        if (payload === undefined) break;
        waiters.shift()(payload);
    }
}

export function waitForEvent() {
    const event = takeEvent();
    return event === undefined
        ? new Promise((resolve) => waiters.push(resolve))
        : Promise.resolve(event);
}

export function takeEventBatch(prefix) {
    const batch = [];
    while (!activeBarrier && events[0]?.payload.startsWith(prefix)) {
        batch.push(takeEvent());
    }
    return batch;
}

export function scheduleEvent(key, payload, delayMilliseconds) {
    cancelScheduledEvent(key);
    scheduledEvents.set(
        key,
        setTimeout(() => {
            scheduledEvents.delete(key);
            emit(payload);
        }, delayMilliseconds),
    );
}

export function cancelScheduledEvent(key) {
    const timer = scheduledEvents.get(key);
    if (timer !== undefined) {
        clearTimeout(timer);
        scheduledEvents.delete(key);
    }
}
