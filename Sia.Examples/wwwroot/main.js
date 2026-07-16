import { dotnet } from './_framework/dotnet.js'

const { setModuleImports, runMain } = await dotnet.create();

const events = [];
const listeners = [];

function emit(eventId) {
    if (listeners.length > 0) {
        listeners.shift()(eventId);
    } else {
        events.push(eventId);
    }
}

setModuleImports('main.js', {
    find(id) {
        return document.getElementById(id);
    },
    create(tag) {
        return document.createElement(tag);
    },
    setText(element, value) {
        if (element.textContent !== value) {
            element.textContent = value;
        }
    },
    toggleClass(element, name, enabled) {
        element.classList.toggle(name, enabled);
    },
    listen(element, name, eventId) {
        element.addEventListener(name, () => emit(eventId));
    },
    insertBefore(parent, child, before) {
        parent.insertBefore(child, before);
    },
    remove(element) {
        element.remove();
    },
    waitForEvent() {
        if (events.length > 0) {
            return Promise.resolve(events.shift());
        }
        return new Promise(resolve => listeners.push(resolve));
    },
});

await runMain();
