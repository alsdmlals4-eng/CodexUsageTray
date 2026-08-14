"use strict";

(function exposeRuntimeMessaging(root, factory) {
  const api = factory();
  root.CodexUsageTrayRuntimeMessaging = api;
  if (typeof module === "object" && module.exports) {
    module.exports = api;
  }
})(globalThis, () => {
  function isContextInvalidated(error) {
    const message = String(error?.message || error || "");
    return message.toLocaleLowerCase().includes("extension context invalidated");
  }

  function sendRuntimeMessage(runtime, message, onContextInvalidated = () => {}) {
    if (!runtime?.id || typeof runtime.sendMessage !== "function") {
      onContextInvalidated();
      return false;
    }

    let pending;
    try {
      pending = runtime.sendMessage(message);
    }
    catch (error) {
      if (isContextInvalidated(error)) {
        onContextInvalidated();
      }
      return false;
    }

    if (pending && typeof pending.catch === "function") {
      pending.catch((error) => {
        if (isContextInvalidated(error)) {
          onContextInvalidated();
        }
      });
    }
    return true;
  }

  return { isContextInvalidated, sendRuntimeMessage };
});
