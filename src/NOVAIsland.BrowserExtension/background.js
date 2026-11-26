/**
 * DOOM Island — MV3 Service Worker
 * Maintains Native Messaging port, arbitrates multiple media tabs, deduplicates state,
 * and routes commands between DOOM Island and active browser media tabs.
 */
const HOST_NAME = 'com.doomisland.host';

let nativePort = null;
let bridgeEnabled = true;
let reconnectAttempt = 0;
let activeMediaTabId = null;

/** @type {Map<number, { tabId: number, windowId: number, isAudible: boolean, isActiveBrowserTab: boolean, snapshot: any, updatedAt: number }>} */
const mediaTabs = new Map();

function detectBrowserName() {
  const ua = navigator.userAgent || '';
  if (ua.includes('Edg/')) return 'Microsoft Edge';
  if (ua.includes('OPR/') || ua.includes('Opera/')) return 'Opera';
  if (ua.includes('Chrome/')) return 'Google Chrome';
  return 'Chromium';
}

function makeEnvelope(type, tabId, windowId, payload) {
  return {
    version: 1,
    type,
    messageId: crypto.randomUUID(),
    timestampUtc: new Date().toISOString(),
    browser: detectBrowserName(),
    tabId: tabId ?? null,
    windowId: windowId ?? null,
    payload: payload ?? {},
  };
}

function sendNative(type, tabId, windowId, payload) {
  if (!bridgeEnabled || !nativePort) return;
  try {
    nativePort.postMessage(makeEnvelope(type, tabId, windowId, payload));
  } catch {
    handleNativeDisconnect();
  }
}

function connectNative() {
  if (!bridgeEnabled || nativePort) return;
  try {
    nativePort = chrome.runtime.connectNative(HOST_NAME);
    reconnectAttempt = 0;

    nativePort.onMessage.addListener((msg) => {
      handleNativeMessage(msg);
    });

    nativePort.onDisconnect.addListener(() => {
      handleNativeDisconnect();
    });

    sendNative('browser_connected', null, null, {
      extensionVersion: chrome.runtime.getManifest().version,
      browserName: detectBrowserName(),
    });

    // Re-request snapshots from all open tabs after browser restart / SW wakeup
    chrome.tabs.query({}, (tabs) => {
      for (const tab of tabs) {
        if (tab.id && tab.url && (tab.url.startsWith('http://') || tab.url.startsWith('https://'))) {
          chrome.tabs.sendMessage(tab.id, { type: 'doom_request_snapshot' }).catch(() => {});
        }
      }
    });
  } catch {
    scheduleReconnect();
  }
}

function handleNativeDisconnect() {
  nativePort = null;
  scheduleReconnect();
}

function scheduleReconnect() {
  if (!bridgeEnabled) return;
  const delayMs = Math.min(15000, 1000 * Math.pow(2, reconnectAttempt));
  reconnectAttempt = Math.min(reconnectAttempt + 1, 5);
  setTimeout(connectNative, delayMs);
}

function selectPrimaryTabId() {
  if (mediaTabs.size === 0) return null;
  const entries = [...mediaTabs.values()];

  // 1. Playing + audible
  const playing = entries
    .filter((t) => t.snapshot?.playbackState === 'playing')
    .sort((a, b) => Number(b.isActiveBrowserTab) - Number(a.isActiveBrowserTab) || b.updatedAt - a.updatedAt);
  if (playing.length > 0) return playing[0].tabId;

  // 2. Active browser tab with paused media
  const activePaused = entries.find((t) => t.isActiveBrowserTab);
  if (activePaused) return activePaused.tabId;

  // 3. Most recently updated tab
  entries.sort((a, b) => b.updatedAt - a.updatedAt);
  return entries[0].tabId;
}

function evaluateActiveTabSwitch() {
  const nextId = selectPrimaryTabId();
  if (nextId !== activeMediaTabId) {
    activeMediaTabId = nextId;
    if (nextId !== null) {
      const entry = mediaTabs.get(nextId);
      sendNative('active_tab_changed', nextId, entry?.windowId ?? null, {
        tabId: nextId,
        origin: entry?.snapshot?.origin ?? '',
        isAudible: entry?.isAudible ?? false,
        isActiveBrowserTab: entry?.isActiveBrowserTab ?? false,
      });
    }
  }
}

async function handleNativeMessage(msg) {
  if (!msg || msg.type !== 'media_command' || !msg.payload) return;
  const { commandId, targetTabId, action, seekPositionSeconds } = msg.payload;
  const tabId = targetTabId || activeMediaTabId;

  if (!tabId) {
    sendNative('command_result', null, null, { commandId, success: false, reason: 'no_active_tab' });
    return;
  }

  if (action === 'focus_tab') {
    try {
      const tab = await chrome.tabs.update(tabId, { active: true });
      if (tab?.windowId) {
        await chrome.windows.update(tab.windowId, { focused: true });
      }
      sendNative('command_result', tabId, tab?.windowId ?? null, { commandId, success: true, reason: 'ok' });
    } catch {
      sendNative('command_result', tabId, null, { commandId, success: false, reason: 'tab_not_found' });
    }
    return;
  }

  try {
    const res = await chrome.tabs.sendMessage(tabId, {
      type: 'doom_execute_command',
      action,
      seekPositionSeconds,
    });
    sendNative('command_result', tabId, null, {
      commandId,
      success: Boolean(res?.success),
      reason: res?.reason || 'ok',
    });
  } catch {
    sendNative('command_result', tabId, null, {
      commandId,
      success: false,
      reason: 'tab_unreachable',
    });
  }
}

chrome.runtime.onMessage.addListener((msg, sender, sendResponse) => {
  if (msg?.type === 'doom_popup_status') {
    const current = activeMediaTabId ? mediaTabs.get(activeMediaTabId) : null;
    sendResponse({
      connected: Boolean(nativePort),
      enabled: bridgeEnabled,
      activeTab: current?.snapshot ?? null,
      tabCount: mediaTabs.size,
    });
    return true;
  }

  if (msg?.type === 'doom_popup_toggle') {
    bridgeEnabled = Boolean(msg.enabled);
    chrome.storage.local.set({ bridgeEnabled });
    if (!bridgeEnabled && nativePort) {
      sendNative('browser_disconnected', null, null, { reason: 'disabled_by_user' });
      nativePort.disconnect();
      nativePort = null;
    } else if (bridgeEnabled) {
      connectNative();
    }
    sendResponse({ ok: true, enabled: bridgeEnabled });
    return true;
  }

  const tab = sender.tab;
  if (!tab?.id || !bridgeEnabled) return false;

  const tabId = tab.id;
  const windowId = tab.windowId;
  const entry = mediaTabs.get(tabId) || {
    tabId,
    windowId,
    isAudible: Boolean(tab.audible),
    isActiveBrowserTab: Boolean(tab.active),
    snapshot: null,
    updatedAt: Date.now(),
  };

  entry.isAudible = Boolean(tab.audible);
  entry.isActiveBrowserTab = Boolean(tab.active);
  entry.updatedAt = Date.now();

  if (msg.kind === 'media_detected' || msg.kind === 'media_metadata_updated') {
    entry.snapshot = msg.snapshot;
    mediaTabs.set(tabId, entry);
    sendNative(msg.kind, tabId, windowId, {
      ...msg.snapshot,
      isAudible: entry.isAudible,
      isActiveBrowserTab: entry.isActiveBrowserTab,
    });
    evaluateActiveTabSwitch();
  } else if (msg.kind === 'playback_state_changed') {
    if (entry.snapshot) {
      entry.snapshot.playbackState = msg.playbackState;
      entry.snapshot.positionSeconds = msg.positionSeconds;
    }
    mediaTabs.set(tabId, entry);
    sendNative('playback_state_changed', tabId, windowId, {
      playbackState: msg.playbackState,
      positionSeconds: msg.positionSeconds,
      playbackRate: msg.playbackRate,
    });
    evaluateActiveTabSwitch();
  } else if (msg.kind === 'playback_position_updated') {
    if (entry.snapshot) {
      entry.snapshot.positionSeconds = msg.positionSeconds;
      entry.snapshot.durationSeconds = msg.durationSeconds;
    }
    sendNative('playback_position_updated', tabId, windowId, {
      positionSeconds: msg.positionSeconds,
      durationSeconds: msg.durationSeconds,
      playbackRate: msg.playbackRate,
    });
  } else if (msg.kind === 'media_stopped') {
    mediaTabs.delete(tabId);
    sendNative('media_stopped', tabId, windowId, { tabId, reason: msg.reason || 'stopped' });
    evaluateActiveTabSwitch();
  }

  return false;
});

chrome.tabs.onRemoved.addListener((tabId) => {
  if (mediaTabs.has(tabId)) {
    mediaTabs.delete(tabId);
    sendNative('media_stopped', tabId, null, { tabId, reason: 'tab_closed' });
    evaluateActiveTabSwitch();
  }
});

chrome.tabs.onActivated.addListener((activeInfo) => {
  for (const [id, entry] of mediaTabs.entries()) {
    entry.isActiveBrowserTab = id === activeInfo.tabId;
  }
  evaluateActiveTabSwitch();
});

chrome.storage.local.get(['bridgeEnabled'], (res) => {
  bridgeEnabled = res.bridgeEnabled !== false;
  if (bridgeEnabled) {
    connectNative();
  }
});

