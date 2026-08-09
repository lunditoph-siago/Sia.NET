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
const editorSurfaceHandlers = new WeakMap();
const overlayPlacements = new WeakMap();
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

  let pendingMutation;
  let knownSelection;
  let composing = false;
  let compositionEnding = false;
  let compositionGeneration = 0;
  let compositionCommitTimer;
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
  const emitCommand = (eventType, selection, payload) => {
    commandSequence++;
    emit(
      `${eventType}:${cellId}:${commandSequence}:`
      + `${selectionArguments(selection)}:${payload}`);
  };
  const readSelection = () => {
    const selection = window.getSelection();
    if (!selection?.rangeCount || !surface.contains(selection.anchorNode)) {
      return null;
    }
    const anchorLine = lineOf(surface, selection.anchorNode);
    const headLine = lineOf(surface, selection.focusNode);
    if (!anchorLine || !headLine
        || anchorLine.parentNode !== surface
        || headLine.parentNode !== surface) {
      return null;
    }
    return {
      anchorLine,
      anchorLineIndex: Number(anchorLine.dataset.ln),
      anchorColumn: columnInLine(
        anchorLine,
        selection.anchorNode,
        selection.anchorOffset),
      headLine,
      headLineIndex: Number(headLine.dataset.ln),
      headColumn: columnInLine(
        headLine,
        selection.focusNode,
        selection.focusOffset),
    };
  };
  const readSelectionByDomOrder = () => {
    const selection = window.getSelection();
    if (!selection?.rangeCount || !surface.contains(selection.anchorNode)) {
      return null;
    }
    const normalizeText = value => value
      .replaceAll(caretMarkerText, '')
      .replaceAll('\r\n', '\n')
      .replaceAll('\r', '\n');
    const topLevelPoint = (node, offset) => {
      let top = node;
      if (top === surface) {
        if (offset === 0) {
          return { line: null, lineIndex: 0, column: 0 };
        }
        top = surface.childNodes[Math.min(offset, surface.childNodes.length) - 1];
        node = top;
        offset = top.nodeType === Node.TEXT_NODE
          ? top.nodeValue.length
          : top.childNodes.length;
      } else {
        while (top?.parentNode && top.parentNode !== surface) {
          top = top.parentNode;
        }
      }
      if (!top || top.parentNode !== surface) {
        return null;
      }

      let lineIndex = 0;
      for (const sibling of surface.childNodes) {
        if (sibling === top) {
          break;
        }
        const text = normalizeText(
          sibling.nodeType === Node.TEXT_NODE
            ? sibling.nodeValue
            : sibling.innerText ?? sibling.textContent ?? '');
        lineIndex += text.split('\n').length;
      }
      const range = document.createRange();
      range.setStart(top, 0);
      range.setEnd(node, offset);
      const lines = normalizeText(range.toString()).split('\n');
      return {
        line: top.nodeType === Node.ELEMENT_NODE ? top : null,
        lineIndex: lineIndex + lines.length - 1,
        column: lines.at(-1).length,
      };
    };
    const anchor = topLevelPoint(selection.anchorNode, selection.anchorOffset);
    const head = topLevelPoint(selection.focusNode, selection.focusOffset);
    if (!anchor || !head) {
      return null;
    }
    return {
      anchorLine: anchor.line,
      anchorLineIndex: anchor.lineIndex,
      anchorColumn: anchor.column,
      headLine: head.line,
      headLineIndex: head.lineIndex,
      headColumn: head.column,
    };
  };
  const selectionCoordinates = selection => selection
    ? {
        anchorLineIndex: selection.anchorLineIndex,
        anchorColumn: selection.anchorColumn,
        headLineIndex: selection.headLineIndex,
        headColumn: selection.headColumn,
      }
    : null;
  const selectionArguments = selection => selection
    ? `${selection.anchorLineIndex}:${selection.anchorColumn}:`
      + `${selection.headLineIndex}:${selection.headColumn}`
    : '-1:-1:-1:-1';
  const sameSelection = (left, right) => left === right
    || left && right
      && left.anchorLineIndex === right.anchorLineIndex
      && left.anchorColumn === right.anchorColumn
      && left.headLineIndex === right.headLineIndex
      && left.headColumn === right.headColumn;
  const rememberSelection = selection => {
    knownSelection = selectionCoordinates(selection);
  };

  const emitSelection = () => {
    const selection = readSelection();
    if (!selection || sameSelection(selection, knownSelection)) {
      return;
    }
    rememberSelection(selection);
    emit(`sel:${cellId}:${selectionArguments(selection)}`);
  };

  const reportSelection = () => {
    if (selectionSyncPending.has(surface)
        || pendingMutation
        || composing
        || compositionEnding) {
      return;
    }
    emitSelection();
  };

  const beginMutation = event => {
    const selection = readSelection();
    const inputType = event.inputType || 'insertText';
    if ((composing || compositionEnding) && pendingMutation) {
      pendingMutation.inputType = inputType;
      pendingMutation.exactText = null;
      if (event.data?.includes('\n') || event.data?.includes('\r')) {
        pendingMutation.line = null;
      }
      return;
    }
    const changesStructure = inputType === 'insertParagraph'
      || inputType === 'insertLineBreak'
      || inputType === 'insertFromPaste'
      || inputType === 'insertFromDrop'
      || event.data?.includes('\n')
      || event.data?.includes('\r');
    const line = !changesStructure
      && selection?.anchorLine === selection?.headLine
      ? selection.anchorLine
      : null;
    const exactText = inputType === 'insertText'
      && typeof event.data === 'string'
      && !event.data.includes('\n')
      && !event.data.includes('\r')
      && line
      ? event.data
      : null;
    pendingMutation = {
      before: selectionCoordinates(selection) ?? knownSelection,
      line,
      structure: [...surface.children],
      inputType,
      exactText,
    };
  };

  const reportMutation = () => {
    clearCaretMarkers(surface, true);
    const mutation = pendingMutation;
    pendingMutation = undefined;
    const structureMatches = mutation
      && mutation.structure?.length === surface.children.length
      && mutation.structure.every((line, index) => line === surface.children[index]);
    const after = structureMatches === false
      ? readSelectionByDomOrder()
      : readSelection();
    rememberSelection(after);
    if (!mutation) {
      return;
    }

    let scope;
    let lineIndex;
    let replacement;
    if (structureMatches
        && mutation.exactText !== null
        && mutation.line?.parentNode === surface) {
      scope = 't';
      lineIndex = Number(mutation.line.dataset.ln);
      replacement = mutation.exactText;
    } else if (structureMatches && mutation.line?.parentNode === surface) {
      scope = 'l';
      lineIndex = Number(mutation.line.dataset.ln);
      replacement = mutation.line.textContent.replaceAll(caretMarkerText, '');
    } else {
      scope = 'd';
      lineIndex = -1;
      replacement = surface.innerText
        .replaceAll(caretMarkerText, '')
        .replaceAll('\r\n', '\n')
        .replaceAll('\r', '\n');
    }
    emit(
      `mut:${cellId}:${scope}:${lineIndex}:`
      + `${selectionArguments(mutation.before)}:`
      + `${selectionArguments(after)}:`
      + `${encodeURIComponent(mutation.inputType)}:`
      + encodeURIComponent(replacement));
  };

  const cancelCompositionCommit = () => {
    if (compositionCommitTimer !== undefined) {
      clearTimeout(compositionCommitTimer);
      compositionCommitTimer = undefined;
    }
  };

  const scheduleCompositionCommit = () => {
    cancelCompositionCommit();
    const generation = ++compositionGeneration;
    compositionCommitTimer = setTimeout(() => {
      compositionCommitTimer = undefined;
      if (composing || generation !== compositionGeneration) {
        return;
      }
      compositionEnding = false;
      reportMutation();
    }, 20);
  };

  const commandSelection = () => hasPendingCommand() ? null : readSelection();
  const emitKeyCommand = (key, control, shift, alt) => emitCommand(
    'key',
    commandSelection(),
    `${key}:${control}:${shift}:${alt}`);
  const emitTextCommand = text => emitCommand(
    'text',
    commandSelection(),
    encodeURIComponent(text));

  const handlers = {
    beforeinput(event) {
      selectionSyncPending.delete(surface);
      const selection = readSelection();
      if (!composing
          && !compositionEnding
          && event.inputType === 'insertText'
          && event.data !== null
          && selection
          && (hasPendingCommand()
            || selection.anchorLine !== selection.headLine)) {
        event.preventDefault();
        pendingMutation = undefined;
        emitTextCommand(event.data);
        return;
      }
      if (!composing
          && !compositionEnding
          && (event.inputType === 'insertParagraph'
            || event.inputType === 'insertLineBreak')) {
        event.preventDefault();
        pendingMutation = undefined;
        emitKeyCommand('Enter', false, false, false);
        return;
      }
      if (!composing
          && !compositionEnding
          && event.inputType === 'deleteContentBackward') {
        event.preventDefault();
        pendingMutation = undefined;
        emitKeyCommand('Backspace', false, false, false);
        return;
      }
      if (!composing
          && !compositionEnding
          && event.inputType === 'deleteWordBackward') {
        event.preventDefault();
        pendingMutation = undefined;
        emitKeyCommand('Backspace', true, false, false);
        return;
      }
      if (!composing
          && !compositionEnding
          && event.inputType === 'deleteContentForward') {
        event.preventDefault();
        pendingMutation = undefined;
        emitKeyCommand('Delete', false, false, false);
        return;
      }
      if (!composing
          && !compositionEnding
          && event.inputType === 'deleteWordForward') {
        event.preventDefault();
        pendingMutation = undefined;
        emitKeyCommand('Delete', true, false, false);
        return;
      }
      if (!composing && !compositionEnding && event.inputType === 'deleteByCut') {
        event.preventDefault();
        pendingMutation = undefined;
        emitTextCommand('');
        return;
      }
      if (!composing
          && !compositionEnding
          && (event.inputType === 'insertFromPaste'
            || event.inputType === 'insertFromDrop')) {
        const text = event.data ?? event.dataTransfer?.getData('text/plain');
        if (text !== undefined && text !== null) {
          event.preventDefault();
          pendingMutation = undefined;
          emitTextCommand(text.replaceAll('\r\n', '\n').replaceAll('\r', '\n'));
          return;
        }
      }
      beginMutation(event);
    },
    pointerdown() {
      selectionSyncPending.delete(surface);
    },
    pointerup: reportSelection,
    selectionchange() {
      if (selectionSyncPending.has(surface)) {
        selectionSyncPending.delete(surface);
        return;
      }
      reportSelection();
    },
    keydown(event) {
      selectionSyncPending.delete(surface);
      if (event.isComposing || composing || compositionEnding) {
        return;
      }
      const control = (event.ctrlKey || event.metaKey) && !event.altKey;
      const key = control && event.key.length === 1
        ? event.key.toLowerCase()
        : event.key;
      const handlesKey = control
        ? controlKeys.has(key)
        : specialKeys.has(key);
      if (handlesKey) {
        event.preventDefault();
        emitKeyCommand(
          key,
          event.ctrlKey || event.metaKey,
          event.shiftKey,
          event.altKey);
      }
    },
    input(event) {
      if (event.isComposing || composing) {
        return;
      }
      pendingMutation ??= {
        before: knownSelection,
        line: null,
        structure: null,
        inputType: event.inputType || 'insertText',
        exactText: null,
      };
      if (compositionEnding) {
        scheduleCompositionCommit();
        return;
      }
      queueMicrotask(() => {
        if (!composing && !compositionEnding) {
          reportMutation();
        }
      });
    },
    compositionstart(event) {
      selectionSyncPending.delete(surface);
      cancelCompositionCommit();
      composing = true;
      compositionEnding = false;
      compositionGeneration++;
      beginMutation(event);
    },
    compositionend() {
      composing = false;
      compositionEnding = true;
      scheduleCompositionCommit();
    },
    dispose() {
      compositionGeneration++;
      cancelCompositionCommit();
    },
    acknowledge(sequence) {
      acknowledgedCommandSequence = Math.max(acknowledgedCommandSequence, sequence);
    },
    rememberSelection,
  };

  surface.addEventListener('beforeinput', handlers.beforeinput);
  surface.addEventListener('pointerdown', handlers.pointerdown);
  surface.addEventListener('pointerup', handlers.pointerup);
  surface.addEventListener('keydown', handlers.keydown);
  surface.addEventListener('input', handlers.input);
  surface.addEventListener('compositionstart', handlers.compositionstart);
  surface.addEventListener('compositionend', handlers.compositionend);
  document.addEventListener('selectionchange', handlers.selectionchange);
  editorHandlers.set(cellId, handlers);
  editorSurfaceHandlers.set(surface, handlers);
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
  handlers.dispose();
  surface.removeEventListener('beforeinput', handlers.beforeinput);
  surface.removeEventListener('pointerdown', handlers.pointerdown);
  surface.removeEventListener('pointerup', handlers.pointerup);
  surface.removeEventListener('keydown', handlers.keydown);
  surface.removeEventListener('input', handlers.input);
  surface.removeEventListener('compositionstart', handlers.compositionstart);
  surface.removeEventListener('compositionend', handlers.compositionend);
  document.removeEventListener('selectionchange', handlers.selectionchange);
  editorHandlers.delete(cellId);
  editorSurfaceHandlers.delete(surface);
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
  editorSurfaceHandlers.get(surface)?.rememberSelection(update);
  selection.setBaseAndExtent(anchor.node, anchor.offset, head.node, head.offset);
  headLine.scrollIntoView({ block: 'nearest', inline: 'nearest' });
  return true;
}

function editorPositionRect(surface, lineIndex, column) {
  const line = surface.querySelector(`[data-ln="${lineIndex}"]`);
  if (!line || column < 0) {
    return null;
  }

  let remaining = column;
  let point = null;
  let lastText = null;
  const walker = document.createTreeWalker(line, NodeFilter.SHOW_TEXT);
  for (let node = walker.nextNode(); node; node = walker.nextNode()) {
    if (node.parentElement?.closest?.(caretMarkerSelector)) {
      continue;
    }
    const length = node.nodeValue.length;
    if (remaining <= length) {
      point = { node, offset: remaining };
      break;
    }
    remaining -= length;
    lastText = node;
  }
  if (!point && remaining === 0 && lastText) {
    point = { node: lastText, offset: lastText.nodeValue.length };
  }
  if (!point && column !== 0) {
    return null;
  }
  point ??= { node: line, offset: 0 };

  const range = document.createRange();
  range.setStart(point.node, point.offset);
  range.collapse(true);
  let rect = range.getBoundingClientRect();
  if (rect.height) {
    return rect;
  }

  if (point.node.nodeType === Node.TEXT_NODE && point.node.nodeValue.length) {
    if (point.offset < point.node.nodeValue.length) {
      range.setEnd(point.node, point.offset + 1);
      rect = range.getBoundingClientRect();
      if (rect.height) {
        return {
          left: rect.left,
          right: rect.left,
          top: rect.top,
          bottom: rect.bottom,
          width: 0,
          height: rect.height,
        };
      }
    } else if (point.offset > 0) {
      range.setStart(point.node, point.offset - 1);
      rect = range.getBoundingClientRect();
      if (rect.height) {
        return {
          left: rect.right,
          right: rect.right,
          top: rect.top,
          bottom: rect.bottom,
          width: 0,
          height: rect.height,
        };
      }
    }
  }

  const lineRect = line.getBoundingClientRect();
  return {
    left: lineRect.left,
    right: lineRect.left,
    top: lineRect.top,
    bottom: lineRect.bottom,
    width: 0,
    height: lineRect.height,
  };
}

function positionOverlay(placement) {
  const { container, surface, overlay, viewport, lineIndex, column } = placement;
  if (!overlay.isConnected) {
    clearOverlayPlacement(overlay);
    return;
  }

  const anchorRect = editorPositionRect(surface, lineIndex, column);
  const viewportRect = viewport.getBoundingClientRect();
  if (!anchorRect
      || anchorRect.bottom < viewportRect.top
      || anchorRect.top > viewportRect.bottom
      || anchorRect.right < viewportRect.left
      || anchorRect.left > viewportRect.right) {
    overlay.style.visibility = 'hidden';
    return;
  }

  const margin = 4;
  const rootFontSize = Number.parseFloat(
    getComputedStyle(document.documentElement).fontSize) || 16;
  const availableWidth = Math.max(0, viewportRect.width - margin * 2);
  if (!availableWidth) {
    overlay.style.visibility = 'hidden';
    return;
  }

  overlay.style.visibility = 'hidden';
  overlay.style.width = `${Math.min(rootFontSize * 22, availableWidth)}px`;
  overlay.style.height = '';
  overlay.style.maxHeight = '12rem';

  const spaceAbove = Math.max(0, anchorRect.top - viewportRect.top - margin);
  const spaceBelow = Math.max(0, viewportRect.bottom - anchorRect.bottom - margin);
  const naturalHeight = overlay.offsetHeight;
  let side = overlay.dataset.overlaySide || 'below';
  const sideSpace = side === 'above' ? spaceAbove : spaceBelow;
  const otherSpace = side === 'above' ? spaceBelow : spaceAbove;
  if (sideSpace < naturalHeight && otherSpace > sideSpace) {
    side = side === 'above' ? 'below' : 'above';
  }
  overlay.dataset.overlaySide = side;

  const availableHeight = side === 'above' ? spaceAbove : spaceBelow;
  overlay.style.maxHeight = `${Math.min(rootFontSize * 12, availableHeight)}px`;
  const overlayRect = overlay.getBoundingClientRect();
  const containerRect = container.getBoundingClientRect();
  const left = Math.max(
    viewportRect.left + margin,
    Math.min(
      anchorRect.left,
      viewportRect.right - overlayRect.width - margin));
  const top = side === 'above'
    ? anchorRect.top - overlayRect.height - margin
    : anchorRect.bottom + margin;
  overlay.style.left = `${left - containerRect.left}px`;
  overlay.style.top = `${top - containerRect.top}px`;
  overlay.style.visibility = '';
}

function placeOverlay(container, surface, overlay, lineIndex, column) {
  let placement = overlayPlacements.get(overlay);
  const viewport = surface.parentElement || container;
  if (placement
      && (placement.container !== container
        || placement.surface !== surface
        || placement.viewport !== viewport)) {
    clearOverlayPlacement(overlay);
    placement = null;
  }
  if (!placement) {
    const controller = new AbortController();
    placement = {
      container,
      surface,
      overlay,
      viewport,
      lineIndex,
      column,
      controller,
      frame: 0,
      schedule: null,
    };
    placement.schedule = () => {
      cancelAnimationFrame(placement.frame);
      placement.frame = requestAnimationFrame(() => positionOverlay(placement));
    };
    viewport.addEventListener('scroll', placement.schedule, {
      passive: true,
      signal: controller.signal,
    });
    window.addEventListener('resize', placement.schedule, {
      passive: true,
      signal: controller.signal,
    });
    overlayPlacements.set(overlay, placement);
  }

  placement.lineIndex = lineIndex;
  placement.column = column;
  cancelAnimationFrame(placement.frame);
  placement.frame = 0;
  positionOverlay(placement);
}

function clearOverlayPlacement(overlay) {
  const placement = overlayPlacements.get(overlay);
  if (!placement) {
    return;
  }
  cancelAnimationFrame(placement.frame);
  placement.controller.abort();
  overlayPlacements.delete(overlay);
  delete overlay.dataset.overlaySide;
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
  clearOverlayPlacement,
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
