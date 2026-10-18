import { emit } from './notebook-runtime.js';

let navigation;

class BrowserNavigation {
    base = new URL(document.baseURI);
    ticket = 0;
    pending = null;
    traversal = null;
    stopped = false;

    constructor() {
        const entry = new URL(location.href);
        const path = entry.searchParams.get('__path');
        if (path) {
            let requested;
            try { requested = new URL(path, this.base); } catch {}
            if (requested?.origin === this.base.origin && !requested.username && !requested.password
                && requested.pathname.startsWith(this.base.pathname)) {
                history.replaceState(history.state, '', requested);
            } else {
                entry.searchParams.delete('__path');
                history.replaceState(history.state, '', entry);
            }
        }
        this.position = this.readPosition() ?? 0;
        this.accepted = this.position;
        this.stamp();
        this.onPop = () => this.pop();
        window.addEventListener('popstate', this.onPop);
    }

    readPath() {
        return location.pathname.startsWith(this.base.pathname)
            ? location.pathname.slice(this.base.pathname.length) : '/invalid';
    }

    readPosition() {
        const state = history.state?.siaNavigation;
        return state?.base === this.base.pathname && Number.isSafeInteger(state.position)
            ? state.position : null;
    }

    stamp() {
        history.replaceState({ ...history.state, siaNavigation: {
            base: this.base.pathname, position: this.position,
        } }, '');
    }

    pop() {
        const previous = this.position;
        this.position = this.readPosition() ?? this.position;
        if (this.traversal) {
            if (this.position === this.traversal.position) {
                const { resolve } = this.traversal;
                this.traversal = null;
                resolve();
            } else {
                if (this.traversal.restore) {
                    this.pending = { ticket: ++this.ticket, position: this.position, path: this.readPath() };
                }
                history.go(this.traversal.position - this.position);
            }
            return;
        }
        if (this.position === this.accepted && previous === this.position) return;
        const request = { ticket: ++this.ticket, position: this.position, path: this.readPath() };
        this.pending = request;
        emit(`navigate:${request.ticket}:${request.path}`);
    }

    move(position, restore = false) {
        if (this.position === position) return Promise.resolve();
        const promise = new Promise(resolve => { this.traversal = { position, restore, resolve }; });
        history.go(position - this.position);
        return promise;
    }

    async commit(path, ticket, replace) {
        if (this.stopped || ticket && ticket !== this.ticket) return;
        const pending = ticket ? this.pending : null;
        if (!ticket && !replace) {
            ++this.ticket;
            this.pending = null;
        }
        if (this.traversal) await new Promise(resolve => {
            const previous = this.traversal.resolve;
            this.traversal.resolve = () => { previous(); resolve(); };
        });
        if (this.stopped || ticket && ticket !== this.ticket) return;
        if (pending) {
            this.accepted = pending.position;
            await this.move(pending.position);
            this.pending = null;
        } else if (!ticket) {
            await this.move(this.accepted);
        }
        const url = new URL(path || './', this.base);
        if (url.origin !== this.base.origin || !url.pathname.startsWith(this.base.pathname)) {
            throw new Error('Navigation must remain inside the application.');
        }
        if (this.readPath() === path || this.readPath() === '' && path === '') {
            return;
        }
        if (!replace && !pending) ++this.position;
        const state = { ...history.state, siaNavigation: { base: this.base.pathname, position: this.position } };
        if (replace || pending) {
            url.search = location.search;
            url.hash = location.hash;
            history.replaceState(state, '', url);
        } else history.pushState(state, '', url);
        this.accepted = this.position;
    }

    async hold(ticket) {
        if (this.stopped || ticket && ticket !== this.ticket) return;
        if (!this.traversal) await this.move(this.accepted, true);
        const pending = this.pending;
        if (!this.stopped && pending && ticket && pending.ticket !== ticket) {
            emit(`navigate:${pending.ticket}:${pending.path}`);
        }
    }

    dispose() {
        this.stopped = true;
        window.removeEventListener('popstate', this.onPop);
        this.traversal?.resolve();
        this.traversal = null;
    }
}

export function startNavigation() {
    navigation?.dispose();
    navigation = new BrowserNavigation();
    return navigation.readPath();
}

export function isCurrentNavigation(ticket) {
    return !!navigation && (!ticket || ticket === navigation.ticket);
}

export function commitNavigation(path, ticket, replace) {
    return navigation.commit(path, ticket, replace);
}

export function holdNavigation(ticket) {
    return navigation.hold(ticket);
}

export function stopNavigation() {
    navigation?.dispose();
    navigation = null;
}

export function showNotFound() {
    const target = new URL('404.html', navigation.base);
    target.searchParams.set('path', location.pathname + location.search + location.hash);
    location.replace(target);
}
