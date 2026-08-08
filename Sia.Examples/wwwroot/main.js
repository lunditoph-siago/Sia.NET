import { dotnet } from './_framework/dotnet.js';

document.getElementById('sidebar-toggle')?.addEventListener('click', () => {
  document.getElementById('app')?.classList.toggle('sidebar-open');
});

document.getElementById('packages-toggle')?.addEventListener('click', event => {
  event.stopPropagation();
  document.getElementById('header-packages')?.classList.toggle('open');
});

document.addEventListener('click', event => {
  const packages = document.getElementById('header-packages');
  if (packages?.classList.contains('open') && !packages.contains(event.target)) {
    packages.classList.remove('open');
  }
});

const events = [];
const waiters = [];
const editorHandlers = new Map();
const resourceRequests = new Map();

function emit(payload) {
  const waiter = waiters.shift();
  if (waiter) {
    waiter(payload);
  } else {
    events.push(payload);
  }
}

function waitForEvent() {
  const event = events.shift();
  return event === undefined
    ? new Promise(resolve => waiters.push(resolve))
    : Promise.resolve(event);
}

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

function lineOf(surface, node) {
  for (let current = node; current && current !== surface; current = current.parentNode) {
    if (current.nodeType === Node.ELEMENT_NODE && current.dataset?.ln !== undefined) {
      return current;
    }
  }
  return null;
}

function columnInLine(line, node, offset) {
  const range = document.createRange();
  range.setStart(line, 0);
  range.setEnd(node, offset);
  return range.toString().length;
}

function attachEditorSurface(cellId, surface) {
  if (editorHandlers.has(cellId)) {
    throw new Error(`Editor '${cellId}' is already attached.`);
  }

  const specialKeys = new Set([
    'Escape',
    'Tab',
    'ArrowUp',
    'ArrowDown',
    'ArrowLeft',
    'ArrowRight',
    'Home',
    'End',
    'PageUp',
    'PageDown',
    'Enter',
    'Backspace',
    'Delete',
  ]);

  const reportSelection = () => {
    const selection = window.getSelection();
    if (!selection?.rangeCount || !surface.contains(selection.anchorNode)) {
      return;
    }
    const anchorLine = lineOf(surface, selection.anchorNode);
    const headLine = lineOf(surface, selection.focusNode);
    if (!anchorLine || !headLine) {
      return;
    }
    emit(
      `sel:${cellId}:${anchorLine.dataset.ln}:`
      + `${columnInLine(anchorLine, selection.anchorNode, selection.anchorOffset)}:`
      + `${headLine.dataset.ln}:`
      + `${columnInLine(headLine, selection.focusNode, selection.focusOffset)}`);
  };

  const reportMutation = () => {
    const selection = window.getSelection();
    const line = selection?.anchorNode
      ? lineOf(surface, selection.anchorNode)
      : null;
    if (line?.parentNode === surface) {
      emit(`mutline:${cellId}:${line.dataset.ln}\0${line.textContent}`);
    } else {
      emit(`mutall:${cellId}:\0${surface.innerText}`);
    }
  };

  const handlers = {
    keydown(event) {
      if (event.isComposing) {
        return;
      }
      const control = (event.ctrlKey || event.metaKey) && !event.altKey;
      if (specialKeys.has(event.key) || control) {
        event.preventDefault();
        emit(
          `key:${cellId}:${event.key}:${event.ctrlKey || event.metaKey}:`
          + `${event.shiftKey}:${event.altKey}`);
      }
    },
    input(event) {
      if (!event.isComposing) {
        reportMutation();
        reportSelection();
      }
    },
    compositionend() {
      reportMutation();
      reportSelection();
    },
    selectionchange: reportSelection,
  };

  surface.addEventListener('keydown', handlers.keydown);
  surface.addEventListener('input', handlers.input);
  surface.addEventListener('compositionend', handlers.compositionend);
  document.addEventListener('selectionchange', handlers.selectionchange);
  editorHandlers.set(cellId, handlers);
}

function detachEditorSurface(cellId, surface) {
  const handlers = editorHandlers.get(cellId);
  if (!handlers) {
    return;
  }
  surface.removeEventListener('keydown', handlers.keydown);
  surface.removeEventListener('input', handlers.input);
  surface.removeEventListener('compositionend', handlers.compositionend);
  document.removeEventListener('selectionchange', handlers.selectionchange);
  editorHandlers.delete(cellId);
}

function setEditorSelection(surface, anchorLineIndex, anchorColumn, headLineIndex, headColumn) {
  const findLine = index => surface.querySelector(`[data-ln="${index}"]`);
  const pointIn = (line, column) => {
    let remaining = column;
    const walker = document.createTreeWalker(line, NodeFilter.SHOW_TEXT);
    let node = walker.nextNode();
    let last = null;
    while (node) {
      if (remaining <= node.nodeValue.length) {
        return { node, offset: remaining };
      }
      remaining -= node.nodeValue.length;
      last = node;
      node = walker.nextNode();
    }
    return last
      ? { node: last, offset: last.nodeValue.length }
      : { node: line, offset: 0 };
  };

  const anchorLine = findLine(anchorLineIndex);
  const headLine = findLine(headLineIndex);
  if (!anchorLine || !headLine) {
    return;
  }
  const anchor = pointIn(anchorLine, anchorColumn);
  const head = pointIn(headLine, headColumn);
  const selection = window.getSelection();
  selection.setBaseAndExtent(anchor.node, anchor.offset, head.node, head.offset);
  headLine.scrollIntoView({ block: 'nearest', inline: 'nearest' });
}

const { setModuleImports, runMain, getConfig, getAssemblyExports } = await dotnet.create();

setModuleImports('main.js', {
  find: id => document.getElementById(id),
  tryFind: id => document.getElementById(id),
  create: tagName => document.createElement(tagName),
  createText: value => document.createTextNode(value),
  setText: (element, value) => { element.textContent = value; },
  getText: element => element.textContent ?? '',
  getValue: element => element.value ?? '',
  setId: (element, id) => { element.id = id; },
  setAttr: (element, name, value) => element.setAttribute(name, value),
  toggleClass: (element, name, enabled) => element.classList.toggle(name, enabled),
  listen: (element, eventName, payload) => {
    element.addEventListener(eventName, () => emit(payload));
  },
  insertBefore: (parent, child, before) => parent.insertBefore(child, before),
  remove: element => element.remove(),
  waitForEvent,
  attachEditorSurface,
  detachEditorSurface,
  setEditorSelection,
  syncGutterScroll(scroll, gutter) {
    scroll.addEventListener('scroll', () => { gutter.scrollTop = scroll.scrollTop; });
  },
  fetchBase64,
  fetchText,
  reportError: message => console.error(message),
});

function showBootError(message) {
  const banner = document.createElement('div');
  banner.className = 'boot-error-banner';
  banner.textContent = message;
  document.body.prepend(banner);
}

async function initializeAssemblyManifest() {
  const config = getConfig();
  const resources = config.resources ?? {};
  const assets = [...(resources.coreAssembly ?? []), ...(resources.assembly ?? [])]
    .filter(asset => asset.resolvedUrl && asset.virtualPath);
  const virtualPaths = assets.map(asset => asset.virtualPath);
  const urls = assets.map(asset => new URL(asset.resolvedUrl, document.baseURI).href);
  const exports = await getAssemblyExports(config.mainAssemblyName);
  await exports.Sia_Examples.Notebook.AssemblyLoader.InitializeAsync(virtualPaths, urls);
}

try {
  await initializeAssemblyManifest();
  await runMain();
} catch (error) {
  console.error(error);
  showBootError('Sia.NET Examples could not start. Reload the page to retry.');
}
