export function configureRuntimeWorkers(runtime) {
    const build = runtime.runtimeBuildInfo;
    if (!build?.wasmEnableThreads || build.productVersion !== '11.0.0-rc.2.26508.101') {
        return;
    }

    const pool = runtime.Module.PThread;
    for (const worker of new Set([...pool.unusedWorkers, ...Object.values(pool.pthreads)])) {
        if (worker.info?.isLoaded) worker.loaded = true;
    }

    const load = pool.loadWasmModuleToWorker;
    pool.loadWasmModuleToWorker = function (worker) {
        const send = worker.postMessage;
        const queued = [];
        worker.postMessage = function (...args) {
            if (args[0]?.cmd === 1) send.apply(this, args);
            else queued.push(args);
        };
        try {
            return load.call(this, worker).then(result => {
                worker.loaded = true;
                worker.postMessage = send;
                for (const args of queued) send.apply(worker, args);
                return result;
            }, error => {
                worker.postMessage = send;
                throw error;
            });
        } catch (error) {
            worker.postMessage = send;
            throw error;
        }
    };

    const borrow = pool.getNewWorker;
    pool.getNewWorker = function () {
        const worker = borrow.call(this);
        const idle = pool.unusedWorkers.indexOf(worker);
        if (idle >= 0) pool.unusedWorkers.splice(idle, 1);
        return worker;
    };
}
