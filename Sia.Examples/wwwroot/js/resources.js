const resourceRequests = new Map();

function loadBytes(url) {
    let request = resourceRequests.get(url);
    if (!request) {
        request = fetch(url).then((response) => {
            if (!response.ok) {
                throw new Error(`HTTP ${response.status} while fetching ${url}`);
            }
            return response.arrayBuffer();
        }).catch((error) => {
            if (resourceRequests.get(url) === request) resourceRequests.delete(url);
            throw error;
        });
        resourceRequests.set(url, request);
    }
    return request;
}

export async function fetchBytes(url) {
    return { bytes: new Uint8Array(await loadBytes(url)) };
}

export async function fetchText(url) {
    return new TextDecoder().decode(await loadBytes(url));
}
