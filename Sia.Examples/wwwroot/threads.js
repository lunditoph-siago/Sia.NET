if (typeof window === "undefined") {
    self.addEventListener("install", () => self.skipWaiting());
    self.addEventListener("activate", event => event.waitUntil(self.clients.claim()));

    self.addEventListener("fetch", event => {
        const request = event.request;
        if (request.cache === "only-if-cached" && request.mode !== "same-origin") {
            return;
        }
        if (new URL(request.url).origin !== self.location.origin) {
            return;
        }

        event.respondWith((async () => {
            const response = await fetch(request);
            if (response.type === "opaque" || response.status === 0) {
                return response;
            }

            const headers = new Headers(response.headers);
            headers.set("Cross-Origin-Opener-Policy", "same-origin");
            headers.set("Cross-Origin-Embedder-Policy", "require-corp");

            return new Response(response.body, {
                status: response.status,
                statusText: response.statusText,
                headers,
            });
        })());
    });
} else {
    (async function boot() {
        const reloadKey = "sia.examples.coop-coep.reload";
        const workerUrl = new URL(window.document.currentScript.src, window.location.href).href;

        async function loadMain() {
            window.sessionStorage.removeItem(reloadKey);
            await import("./main.js");
        }

        if (window.crossOriginIsolated
            && navigator.serviceWorker?.controller?.scriptURL === workerUrl) {
            await loadMain();
            return;
        }

        if (!("serviceWorker" in navigator)) {
            if (window.crossOriginIsolated) {
                await loadMain();
                return;
            }
            throw new Error("Service workers are unavailable; threaded WebAssembly cannot start.");
        }

        await navigator.serviceWorker.register(
            workerUrl,
            { updateViaCache: "none" });

        if (navigator.serviceWorker.controller?.scriptURL !== workerUrl) {
            await new Promise((resolve, reject) => {
                const timeout = window.setTimeout(() => {
                    navigator.serviceWorker.removeEventListener("controllerchange", controllerChanged);
                    reject(new Error("The isolation service worker did not take control."));
                }, 10000);
                function controllerChanged() {
                    if (navigator.serviceWorker.controller?.scriptURL !== workerUrl) {
                        return;
                    }
                    window.clearTimeout(timeout);
                    navigator.serviceWorker.removeEventListener("controllerchange", controllerChanged);
                    resolve();
                }
                navigator.serviceWorker.addEventListener("controllerchange", controllerChanged);
                controllerChanged();
            });
        }

        if (window.crossOriginIsolated) {
            await loadMain();
            return;
        }

        if (window.sessionStorage.getItem(reloadKey) === "1") {
            window.sessionStorage.removeItem(reloadKey);
            throw new Error("The isolation service worker is active, but the page is not cross-origin isolated.");
        }

        window.sessionStorage.setItem(reloadKey, "1");
        window.location.reload();
    })().catch(error => {
        console.error("Failed to start threaded WebAssembly page:", error);
    });
}
