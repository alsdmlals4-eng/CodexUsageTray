"use strict";

(function exposeTabFocus(root, factory) {
  const api = factory();
  root.CodexUsageTrayTabFocus = api;
  if (typeof module === "object" && module.exports) {
    module.exports = api;
  }
})(globalThis, () => {
  function normalizeChatGptUrl(value) {
    const url = new URL(value);
    if (url.protocol !== "https:" || url.hostname !== "chatgpt.com" || url.port) {
      throw new Error("Only HTTPS ChatGPT URLs can be activated");
    }

    url.search = "";
    url.hash = "";
    return url.toString();
  }

  function createActivationPlan(tabs, targetUrl, preferredTabId) {
    const normalizedTarget = normalizeChatGptUrl(targetUrl);
    const matches = tabs.filter((tab) => {
      try {
        return normalizeChatGptUrl(tab.url) === normalizedTarget;
      }
      catch {
        return false;
      }
    });
    const preferred = matches.find((tab) => tab.id === preferredTabId);
    const selected = preferred ?? matches[0];
    if (selected) {
      return {
        action: "focus",
        tabId: selected.id,
        windowId: selected.windowId
      };
    }

    return { action: "create", url: normalizedTarget };
  }

  function createReloadPlan(tabs, targetUrl, preferredTabId) {
    const normalizedTarget = normalizeChatGptUrl(targetUrl);
    const preferred = tabs.find((tab) => tab.id === preferredTabId);
    if (!preferred) {
      return null;
    }

    try {
      if (normalizeChatGptUrl(preferred.url) !== normalizedTarget) {
        return null;
      }
    }
    catch {
      return null;
    }

    return { action: "reload", tabId: preferred.id };
  }

  return {
    createActivationPlan,
    createReloadPlan,
    normalizeChatGptUrl
  };
});
