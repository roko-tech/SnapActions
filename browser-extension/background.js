const host = "com.snapactions.selection";
let port;
let retry;
let navigationEpoch = 0;

chrome.tabs.onActivated.addListener(() => navigationEpoch++);
chrome.tabs.onUpdated.addListener((_id, change) => {
  if (change.status === "loading") navigationEpoch++;
});
chrome.windows.onFocusChanged.addListener(() => navigationEpoch++);

async function focusedTab() {
  const windows = await chrome.windows.getAll({ windowTypes: ["normal", "popup"] });
  const focused = windows.filter(window => window.focused);
  if (focused.length !== 1) return null;
  const tabs = await chrome.tabs.query({ active: true, windowId: focused[0].id });
  return tabs.length === 1 ? tabs[0] : null;
}

async function readSelection(id, identity) {
  const epoch = navigationEpoch;
  const tab = await focusedTab();
  if (!tab) return { id, status: "inactive" };
  let expected;
  if (identity != null) {
    try { expected = JSON.parse(identity); } catch { return { id, status: "unavailable" }; }
    if (!Array.isArray(expected) || expected.length !== 4 || expected[0] !== tab.id ||
        !Number.isInteger(expected[1]) || typeof expected[2] !== "string") return { id, status: "inactive" };
  }
  const results = await chrome.scripting.executeScript({
    target: expected ? { tabId: tab.id, documentIds: [expected[2]] } : { tabId: tab.id, allFrames: true },
    files: ["read-selection.js"]
  });
  const current = await focusedTab();
  if (navigationEpoch !== epoch || current?.id !== tab.id || current?.windowId !== tab.windowId)
    return { id, status: "inactive" };
  const selected = results.filter(item => item.result && item.documentId);
  if (selected.length !== 1) return { id, status: "empty" };
  const frame = selected[0];
  if (expected && (frame.frameId !== expected[1] || frame.documentId !== expected[2] || frame.result.identity !== expected[3]))
    return { id, status: "inactive" };
  return {
    id, status: "ok", text: frame.result.text, editable: frame.result.editable,
    direction: frame.result.direction,
    identity: JSON.stringify([tab.id, frame.frameId, frame.documentId, frame.result.identity])
  };
}

function connect() {
  if (port) return;
  clearTimeout(retry);
  const connection = chrome.runtime.connectNative(host);
  port = connection;
  connection.onMessage.addListener(async message => {
    if (message.type !== "selection" || !Number.isSafeInteger(message.id)) return;
    let response;
    try { response = message.version === 1 ? await readSelection(message.id, message.identity) : { id: message.id, status: "incompatible" }; }
    catch { response = { id: message.id, status: "unavailable" }; }
    // Never deliver a reply from an old port on its replacement connection.
    if (port === connection) {
      try { connection.postMessage({ ...response, version: 1 }); } catch { /* Disconnected meanwhile. */ }
    }
  });
  connection.onDisconnect.addListener(() => {
    void chrome.runtime.lastError;
    if (port !== connection) return;
    port = null;
    retry = setTimeout(connect, 1000);
  });
}

chrome.runtime.onStartup.addListener(connect);
chrome.runtime.onInstalled.addListener(connect);
chrome.action.onClicked.addListener(connect);
connect();
