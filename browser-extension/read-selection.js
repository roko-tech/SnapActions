// Runs in the extension's isolated world only when the desktop requests a selection.
(() => {
  if (!document.hasFocus()) return null;
  let active = document.activeElement;
  while (active?.shadowRoot?.activeElement) active = active.shadowRoot.activeElement;
  if (active?.tagName === "IFRAME" || active?.tagName === "FRAME") return null;

  function path(node) {
    const parts = [];
    while (node && node !== document) {
      const parent = node.parentNode;
      if (!parent && node.host) { parts.push("shadow"); node = node.host; continue; }
      if (!parent) return null;
      parts.push(Array.prototype.indexOf.call(parent.childNodes, node));
      node = parent;
    }
    return parts;
  }

  let text, editable, key, directionElement = active;
  if (active?.tagName === "INPUT" || active?.tagName === "TEXTAREA") {
    if (active.tagName === "INPUT" && !["text", "search", "url", "tel"].includes(active.type)) return null;
    if (!Number.isInteger(active.selectionStart) || !Number.isInteger(active.selectionEnd)) return null;
    text = active.value.slice(active.selectionStart, active.selectionEnd);
    editable = !active.readOnly && !active.disabled;
    key = [path(active), active.selectionStart, active.selectionEnd];
  } else {
    const selection = window.getSelection();
    if (!selection || selection.isCollapsed) return null;
    text = selection.toString();
    const node = selection.getRangeAt(0).startContainer;
    directionElement = node.nodeType === Node.ELEMENT_NODE ? node : node.parentElement;
    editable = active?.isContentEditable === true;
    key = [path(selection.anchorNode), selection.anchorOffset, path(selection.focusNode), selection.focusOffset];
  }
  if (!text || text.length > 32768) return null;
  const identity = JSON.stringify(key);
  if (identity.length > 8000) return null;
  const direction = getComputedStyle(directionElement || document.documentElement).direction;
  return { text, editable, identity, direction };
})();
