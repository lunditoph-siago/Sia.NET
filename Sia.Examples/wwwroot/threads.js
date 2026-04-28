if (typeof window === "undefined") {
    self.addEventListener("install", () => self.skipWaiting());
    self.addEventListener("activate", event => event.waitUntil(self.clients.claim()));

    async function handleFetch(request) {
        if (request.cache === "only-if-cached" && request.mode !== "same-origin") {
            return fetch(request);
        }

        if (request.mode === "no-cors") {
            request = new Request(request.url, {
                cache: request.cache,
                credentials: "omit",
                headers: request.headers,
                integrity: request.integrity,
                destination: request.destination,
                keepalive: request.keepalive,
                method: request.method,
                mode: request.mode,
                redirect: request.redirect,
                referrer: request.referrer,
                referrerPolicy: request.referrerPolicy,
                signal: request.signal,
            });
        }

        const response = await fetch(request);
        if (response.type === "opaque" || response.status === 0) {
            return response;
        }

        const headers = new Headers(response.headers);
        headers.set("Cross-Origin-Embedder-Policy", "credentialless");
        headers.set("Cross-Origin-Opener-Policy", "same-origin");

        return new Response(response.body, {
            status: response.status,
            statusText: response.statusText,
            headers,
        });
    }

    self.addEventListener("fetch", event => {
        event.respondWith(handleFetch(event.request));
    });
} else {
    (async function boot() {
        const reloadKey = "sia.examples.coop-coep.reload";

        async function loadMain() {
            window.sessionStorage.removeItem(reloadKey);
            await import("./main.js");
        }

        function reloadForIsolation() {
            if (window.sessionStorage.getItem(reloadKey) === "1") {
                console.error("COOP/COEP Service Worker is active, but the page is still not cross-origin isolated.");
                return;
            }

            window.sessionStorage.setItem(reloadKey, "1");
            window.location.reload();
        }

        if (window.crossOriginIsolated) {
            await loadMain();
            return;
        }

        if (!("serviceWorker" in navigator)) {
            console.error("Service workers are not available; threaded WebAssembly cannot start.");
            return;
        }

        const registration = await navigator.serviceWorker.register(
            window.document.currentScript.src,
            { updateViaCache: "none" });

        console.log("COOP/COEP Service Worker registered", registration.scope);

        if (!navigator.serviceWorker.controller) {
            await new Promise(resolve => {
                navigator.serviceWorker.addEventListener("controllerchange", resolve, { once: true });
            });
        }

        reloadForIsolation();
    })().catch(error => {
        console.error("Failed to start threaded WebAssembly page:", error);
    });
}
