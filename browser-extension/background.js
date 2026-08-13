"use strict";

importScripts("tab-focus.js");

const NATIVE_HOST = "com.alsdmlals4.codexusagetray";
let nativePort = null;
let reconnectTimer = null;

function scheduleReconnect() {
  if (reconnectTimer !== null) {
    return;
  }

  reconnectTimer = setTimeout(() => {
    reconnectTimer = null;
    connectNativeHost();
  }, 1000);
}

function connectNativeHost() {
  if (nativePort !== null) {
    return nativePort;
  }

  try {
    const port = chrome.runtime.connectNative(NATIVE_HOST);
    nativePort = port;
    port.onMessage.addListener((message) => {
      if (message?.action === "activate") {
        activateChatGptSource(message).catch(() => {
          // The source may close between query and activation; a future click can retry.
        });
      }
    });
    port.onDisconnect.addListener(() => {
      void chrome.runtime.lastError;
      if (nativePort === port) {
        nativePort = null;
      }
      scheduleReconnect();
    });
    return port;
  }
  catch {
    scheduleReconnect();
    return null;
  }
}

async function activateChatGptSource(message) {
  const tabs = await chrome.tabs.query({ url: "https://chatgpt.com/*" });
  const plan = CodexUsageTrayTabFocus.createActivationPlan(
    tabs,
    message.url,
    message.tabId);
  if (plan.action === "create") {
    await chrome.tabs.create({ url: plan.url });
    return;
  }

  await chrome.tabs.update(plan.tabId, { active: true });
  const sourceWindow = await chrome.windows.get(plan.windowId);
  if (sourceWindow.state === "minimized") {
    await chrome.windows.update(plan.windowId, { state: "normal" });
  }
  await chrome.windows.update(plan.windowId, { focused: true });
}

chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  const senderUrl = sender.url || sender.tab?.url || "";
  if (message?.type !== "codex-usage-tray-activity" ||
      !senderUrl.startsWith("https://chatgpt.com/") ||
      !Number.isInteger(sender.tab?.id) || sender.tab.id <= 0 ||
      !Number.isInteger(sender.tab?.windowId) || sender.tab.windowId <= 0) {
    return false;
  }

  const port = connectNativeHost();
  if (port === null) {
    sendResponse({ ok: false });
    return false;
  }

  port.postMessage({
    ...message.activity,
    tabId: sender.tab.id,
    windowId: sender.tab.windowId
  });
  sendResponse({ ok: true });
  return true;
});

connectNativeHost();
