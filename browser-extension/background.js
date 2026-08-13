"use strict";

const NATIVE_HOST = "com.alsdmlals4.codexusagetray";

chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  const senderUrl = sender.url || sender.tab?.url || "";
  if (message?.type !== "codex-usage-tray-activity" ||
      !senderUrl.startsWith("https://chatgpt.com/")) {
    return false;
  }

  chrome.runtime.sendNativeMessage(NATIVE_HOST, message.activity, (response) => {
    const error = chrome.runtime.lastError;
    sendResponse({ ok: !error && response?.ok === true });
  });
  return true;
});
