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
const scheduledEvents = new Map();
const selectionSyncPending = new WeakSet();
const editorSelectionUpdates = new WeakMap();
const caretMarkerText = '\u200b';
const caretMarkerSelector = '[data-editor-caret-marker]';

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

function scheduleEvent(key, payload, delayMilliseconds) {
  cancelScheduledEvent(key);
  scheduledEvents.set(key, setTimeout(() => {
    scheduledEvents.delete(key);
    emit(payload);
  }, delayMilliseconds));
}

function cancelScheduledEvent(key) {
  const timer = scheduledEvents.get(key);
  if (timer !== undefined) {
    clearTimeout(timer);
    scheduledEvents.delete(key);
  }
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
  return range.toString().replaceAll(caretMarkerText, '').length;
}

function clearCaretMarkers(surface, preserveSelection) {
  const markers = [...surface.querySelectorAll(caretMarkerSelector)];
  if (!preserveSelection) {
    markers.forEach(marker => marker.remove());
    return;
  }

  const selection = window.getSelection();
  const pointInMarker = (node, offset) => {
    const element = node?.nodeType === Node.ELEMENT_NODE ? node : node?.parentElement;
    const marker = element?.closest?.(caretMarkerSelector);
    if (!marker || !surface.contains(marker)) {
      return null;
    }
    const range = document.createRange();
    range.setStart(marker, 0);
    range.setEnd(node, offset);
    return {
      marker,
      offset: range.toString().replaceAll(caretMarkerText, '').length,
    };
  };
  const anchor = pointInMarker(selection?.anchorNode, selection?.anchorOffset ?? 0);
  const head = pointInMarker(selection?.focusNode, selection?.focusOffset ?? 0);
  const replacements = new Map();
  for (const marker of markers) {
    const text = marker.textContent.replaceAll(caretMarkerText, '');
    if (text) {
      const node = document.createTextNode(text);
      replacements.set(marker, { node });
      marker.replaceWith(node);
    } else {
      replacements.set(marker, {
        line: marker.closest('[data-ln]'),
        position: marker.dataset.editorCaretMarker,
      });
      marker.remove();
    }
  }
  if (anchor || head) {
    const boundaryPoint = replacement => {
      const walker = document.createTreeWalker(replacement.line, NodeFilter.SHOW_TEXT);
      let first = null;
      let last = null;
      for (let node = walker.nextNode(); node; node = walker.nextNode()) {
        first ??= node;
        last = node;
      }
      if (replacement.position === 'start' && first) {
        return { node: first, offset: 0 };
      }
      if (replacement.position === 'end' && last) {
        return { node: last, offset: last.nodeValue.length };
      }
      return {
        node: replacement.line,
        offset: replacement.position === 'start'
          ? 0
          : replacement.line.childNodes.length,
      };
    };
    const mappedPoint = (point, node, offset) => {
      if (!point) {
        return { node, offset };
      }
      const replacement = replacements.get(point.marker);
      if (replacement.node) {
        return {
          node: replacement.node,
          offset: Math.min(point.offset, replacement.node.nodeValue.length),
        };
      }
      return boundaryPoint(replacement);
    };
    const mappedAnchor = mappedPoint(anchor, selection.anchorNode, selection.anchorOffset);
    const mappedHead = mappedPoint(head, selection.focusNode, selection.focusOffset);
    selection.setBaseAndExtent(
      mappedAnchor.node,
      mappedAnchor.offset,
      mappedHead.node,
      mappedHead.offset);
  }
}

function attachEditorSurface(cellId, surface) {
  if (editorHandlers.has(cellId)) {
    throw new Error(`Editor '${cellId}' is already attached.`);
  }

  let pendingMutationLine;
  let pendingTextInput;
  let commandSequence = 0;
  let acknowledgedCommandSequence = 0;

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
  const controlKeys = new Set([
    'a',
    'k',
    'ArrowLeft',
    'ArrowRight',
    'Home',
    'End',
    'Backspace',
    'Delete',
  ]);
  const hasPendingCommand = () => acknowledgedCommandSequence < commandSequence;
  const emitCommand = (eventType, payload) => {
    commandSequence++;
    emit(`${eventType}:${cellId}:${commandSequence}:${payload}`);
  };
  const readSelection = () => {
    const selection = window.getSelection();
    return {
      selection,
      anchorLine: selection?.anchorNode
        ? lineOf(surface, selection.anchorNode)
        : null,
      headLine: selection?.focusNode
        ? lineOf(surface, selection.focusNode)
        : null,
    };
  };

  const emitSelection = () => {
    const { selection, anchorLine, headLine } = readSelection();
    if (!selection?.rangeCount || !surface.contains(selection.anchorNode)) {
      return;
    }
    if (!anchorLine || !headLine) {
      return;
    }
    emit(
      `sel:${cellId}:${anchorLine.dataset.ln}:`
      + `${columnInLine(anchorLine, selection.anchorNode, selection.anchorOffset)}:`
      + `${headLine.dataset.ln}:`
      + `${columnInLine(headLine, selection.focusNode, selection.focusOffset)}`);
  };

  const reportSelection = () => {
    if (!selectionSyncPending.has(surface)) {
      emitSelection();
    }
  };

  const captureMutation = event => {
    const { selection, anchorLine, headLine } = readSelection();
    if (event.inputType === 'insertText'
        && event.data !== null
        && !event.data.includes('\n')
        && !event.data.includes('\r')
        && selection?.rangeCount
        && anchorLine === headLine
        && anchorLine?.parentNode === surface) {
      const anchorColumn = columnInLine(
        anchorLine,
        selection.anchorNode,
        selection.anchorOffset);
      const headColumn = columnInLine(
        headLine,
        selection.focusNode,
        selection.focusOffset);
      pendingTextInput = {
        line: anchorLine,
        from: Math.min(anchorColumn, headColumn),
        to: Math.max(anchorColumn, headColumn),
        text: event.data,
      };
    }
    if (pendingMutationLine !== undefined) {
      return;
    }
    const changesStructure = event.inputType === 'insertParagraph'
      || event.inputType === 'insertLineBreak'
      || event.inputType === 'insertFromPaste'
      || event.inputType === 'insertFromDrop'
      || event.data?.includes('\n')
      || event.data?.includes('\r');
    pendingMutationLine = !changesStructure
      && anchorLine === headLine
      && anchorLine?.parentNode === surface
      ? anchorLine
      : null;
  };

  const reportMutation = () => {
    clearCaretMarkers(surface, true);
    const textInput = pendingTextInput;
    pendingTextInput = undefined;
    const selection = window.getSelection();
    const line = pendingMutationLine === undefined
      ? selection?.anchorNode
        ? lineOf(surface, selection.anchorNode)
        : null
      : pendingMutationLine;
    pendingMutationLine = undefined;
    if (textInput?.line.parentNode === surface) {
      emit(
        `muttext:${cellId}:${textInput.line.dataset.ln}:`
        + `${textInput.from}:${textInput.to}:`
        + encodeURIComponent(textInput.text));
    } else if (line?.parentNode === surface) {
      emit(`mutline:${cellId}:${line.dataset.ln}\0${line.textContent}`);
    } else {
      selectionSyncPending.add(surface);
      emit(`mutall:${cellId}:\0${surface.innerText}`);
    }
  };

  const handlers = {
    beforeinput(event) {
      selectionSyncPending.delete(surface);
      pendingTextInput = undefined;
      const { anchorLine, headLine } = readSelection();
      if (event.inputType === 'insertText'
          && event.data !== null
          && anchorLine
          && headLine
          && (hasPendingCommand() || anchorLine !== headLine)) {
        event.preventDefault();
        pendingMutationLine = undefined;
        emitCommand('text', encodeURIComponent(event.data));
        return;
      }
      captureMutation(event);
    },
    pointerdown() {
      selectionSyncPending.delete(surface);
    },
    pointerup: reportSelection,
    keydown(event) {
      selectionSyncPending.delete(surface);
      if (event.isComposing) {
        return;
      }
      const control = (event.ctrlKey || event.metaKey) && !event.altKey;
      const key = control && event.key.length === 1
        ? event.key.toLowerCase()
        : event.key;
      if (!control
          && !event.altKey
          && event.key.length === 1
          && !hasPendingCommand()) {
        captureMutation(event);
      }
      const handlesKey = control
        ? controlKeys.has(key)
        : specialKeys.has(key);
      if (handlesKey) {
        event.preventDefault();
        emitCommand(
          'key',
          `${key}:${event.ctrlKey || event.metaKey}:`
          + `${event.shiftKey}:${event.altKey}`);
      }
    },
    input(event) {
      if (!event.isComposing) {
        reportMutation();
      }
    },
    compositionend() {
      reportMutation();
    },
    acknowledge(sequence) {
      acknowledgedCommandSequence = Math.max(acknowledgedCommandSequence, sequence);
    },
  };

  surface.addEventListener('beforeinput', handlers.beforeinput);
  surface.addEventListener('pointerdown', handlers.pointerdown);
  surface.addEventListener('pointerup', handlers.pointerup);
  surface.addEventListener('keydown', handlers.keydown);
  surface.addEventListener('input', handlers.input);
  surface.addEventListener('compositionend', handlers.compositionend);
  editorHandlers.set(cellId, handlers);
}

function detachEditorSurface(cellId, surface) {
  const handlers = editorHandlers.get(cellId);
  if (!handlers) {
    return;
  }
  const selectionUpdate = editorSelectionUpdates.get(surface);
  if (selectionUpdate) {
    cancelAnimationFrame(selectionUpdate.frame);
    editorSelectionUpdates.delete(surface);
  }
  selectionSyncPending.delete(surface);
  surface.removeEventListener('beforeinput', handlers.beforeinput);
  surface.removeEventListener('pointerdown', handlers.pointerdown);
  surface.removeEventListener('pointerup', handlers.pointerup);
  surface.removeEventListener('keydown', handlers.keydown);
  surface.removeEventListener('input', handlers.input);
  surface.removeEventListener('compositionend', handlers.compositionend);
  editorHandlers.delete(cellId);
}

function acknowledgeEditorCommand(cellId, sequence) {
  editorHandlers.get(cellId)?.acknowledge(sequence);
}

function setEditorSelection(surface, anchorLineIndex, anchorColumn, headLineIndex, headColumn) {
  const previous = editorSelectionUpdates.get(surface);
  if (previous) {
    cancelAnimationFrame(previous.frame);
    editorSelectionUpdates.delete(surface);
  }
  const update = {
    anchorLineIndex,
    anchorColumn,
    headLineIndex,
    headColumn,
    frame: 0,
  };
  if (!applyEditorSelection(surface, update)) {
    scheduleEditorSelection(surface, update);
  }
}

function scheduleEditorSelection(surface, update) {
  update.frame = requestAnimationFrame(() => {
    if (editorSelectionUpdates.get(surface) !== update) {
      return;
    }
    editorSelectionUpdates.delete(surface);
    if (!applyEditorSelection(surface, update)) {
      scheduleEditorSelection(surface, update);
    }
  });
  editorSelectionUpdates.set(surface, update);
}

function applyEditorSelection(surface, update) {
  const { anchorLineIndex, anchorColumn, headLineIndex, headColumn } = update;
  clearCaretMarkers(surface, false);
  const findLine = index => surface.querySelector(`[data-ln="${index}"]`);
  const pointIn = (line, column) => {
    const lineLength = line.textContent.replaceAll(caretMarkerText, '').length;
    if (column > lineLength) {
      return null;
    }
    if (column === 0 || column === lineLength) {
      const position = column === 0 ? 'start' : 'end';
      let marker = line.querySelector(
        `[data-editor-caret-marker="${position}"]`);
      if (!marker) {
        marker = document.createElement('span');
        marker.dataset.editorCaretMarker = position;
        marker.textContent = caretMarkerText;
        if (position === 'start') {
          line.prepend(marker);
        } else {
          line.append(marker);
        }
      }
      return {
        node: marker.firstChild,
        offset: position === 'start' ? caretMarkerText.length : 0,
      };
    }

    let remaining = column;
    const walker = document.createTreeWalker(line, NodeFilter.SHOW_TEXT);
    let node = walker.nextNode();
    let last = null;
    while (node) {
      if (node.parentElement?.closest?.(caretMarkerSelector)) {
        node = walker.nextNode();
        continue;
      }
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
    selectionSyncPending.delete(surface);
    return true;
  }
  const anchor = pointIn(anchorLine, anchorColumn);
  const head = pointIn(headLine, headColumn);
  if (!anchor || !head) {
    return false;
  }
  const selection = window.getSelection();
  selectionSyncPending.add(surface);
  selection.setBaseAndExtent(anchor.node, anchor.offset, head.node, head.offset);
  headLine.scrollIntoView({ block: 'nearest', inline: 'nearest' });
  return true;
}

function placeOverlay(container, anchor, overlay) {
  const containerRect = container.getBoundingClientRect();
  const selection = window.getSelection();
  let anchorRect = anchor.getBoundingClientRect();
  if (selection?.rangeCount && anchor.contains(selection.focusNode)) {
    const range = selection.getRangeAt(0).cloneRange();
    range.collapse(false);
    const selectionRect = range.getBoundingClientRect();
    if (selectionRect.width || selectionRect.height) {
      anchorRect = selectionRect;
    }
  }

  overlay.style.visibility = 'hidden';
  const margin = 4;
  const overlayWidth = overlay.offsetWidth;
  const overlayHeight = overlay.offsetHeight;
  const preferredTop = anchorRect.bottom - containerRect.top + margin;
  const top = preferredTop + overlayHeight <= container.clientHeight
    ? preferredTop
    : Math.max(margin, anchorRect.top - containerRect.top - overlayHeight - margin);
  const left = Math.max(
    42,
    Math.min(
      anchorRect.left - containerRect.left,
      container.clientWidth - overlayWidth - margin));
  overlay.style.top = `${top}px`;
  overlay.style.left = `${left}px`;
  overlay.style.visibility = '';
}

function ensureVisible(container, element) {
  const top = element.offsetTop;
  const bottom = top + element.offsetHeight;
  if (top < container.scrollTop) {
    container.scrollTop = top;
  } else if (bottom > container.scrollTop + container.clientHeight) {
    container.scrollTop = bottom - container.clientHeight;
  }
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
  scheduleEvent,
  cancelScheduledEvent,
  attachEditorSurface,
  detachEditorSurface,
  acknowledgeEditorCommand,
  setEditorSelection,
  placeOverlay,
  ensureVisible,
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
