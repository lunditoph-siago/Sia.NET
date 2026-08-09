import { dotnet } from './_framework/dotnet.js';

const log = document.getElementById('log');
const status = document.getElementById('status');
const banner = document.getElementById('banner');
const resourceRequests = new Map();

function append(kind, values) {
  const line = document.createElement('div');
  line.className = kind;
  line.textContent = values.join(' ');
  log.appendChild(line);
  line.scrollIntoView({ block: 'nearest' });
}

const originalConsole = {
  log: console.log,
  warn: console.warn,
  error: console.error,
};
console.log = (...values) => {
  originalConsole.log(...values);
  append('info', values);
};
console.warn = (...values) => {
  originalConsole.warn(...values);
  append('warn', values);
};
console.error = (...values) => {
  originalConsole.error(...values);
  append('fail', values);
};

function loadBytes(url) {
  let request = resourceRequests.get(url);
  if (!request) {
    request = fetch(url).then(response => {
      if (!response.ok) {
        throw new Error(`HTTP ${response.status} while fetching ${url}`);
      }
      return response.arrayBuffer();
    });
    resourceRequests.set(url, request);
  }
  return request;
}

async function fetchBase64(url) {
  const bytes = new Uint8Array(await loadBytes(url));
  let binary = '';
  for (let offset = 0; offset < bytes.length; offset += 8192) {
    binary += String.fromCharCode(...bytes.subarray(offset, offset + 8192));
  }
  return btoa(binary);
}

async function fetchText(url) {
  return new TextDecoder().decode(await loadBytes(url));
}

function showResult(exitCode) {
  const passed = exitCode === 0;
  status.textContent = passed ? 'All acceptance stages passed.' : 'Acceptance failed.';
  banner.className = `banner ${passed ? 'ok' : 'err'}`;
  banner.textContent = passed
    ? 'All staged acceptance checks passed.'
    : `The acceptance process exited with code ${exitCode}.`;
  banner.hidden = false;
}

try {
  status.textContent = 'Loading the .NET browser runtime…';
  const { setModuleImports, runMain, getConfig, getAssemblyExports } = await dotnet.create();
  setModuleImports('main.js', { fetchBase64, fetchText });

  const config = getConfig();
  const resources = config.resources ?? {};
  const assets = [...(resources.coreAssembly ?? []), ...(resources.assembly ?? [])]
    .filter(asset => asset.resolvedUrl && asset.virtualPath);
  const virtualPaths = assets.map(asset => asset.virtualPath);
  const urls = assets.map(asset => new URL(asset.resolvedUrl, document.baseURI).href);
  const exports = await getAssemblyExports(config.mainAssemblyName);
  await exports.Sia_Examples.Notebook.AssemblyLoader.InitializeAsync(virtualPaths, urls);

  status.textContent = 'Running staged acceptance…';
  showResult(await runMain());
} catch (error) {
  console.error('Browser acceptance boot failed:', error);
  status.textContent = 'Browser acceptance could not start.';
  banner.className = 'banner err';
  banner.textContent = error?.message ?? String(error);
  banner.hidden = false;
}
