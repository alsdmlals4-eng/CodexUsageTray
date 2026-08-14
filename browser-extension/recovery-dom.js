"use strict";

(function exposeRecoveryDom(root, factory) {
  const api = factory();
  root.CodexUsageTrayRecoveryDom = api;
  if (typeof module === "object" && module.exports) {
    module.exports = api;
  }
})(globalThis, () => {
  const SAFE_RETRY_LABELS = new Set([
    "다시 시도",
    "재시도",
    "다시 생성",
    "retry",
    "try again",
    "regenerate",
    "retry response",
    "regenerate response"
  ]);
  const MAX_ANCESTOR_DEPTH = 6;
  const MAX_SURFACE_TEXT_LENGTH = 1600;

  function normalize(value) {
    return String(value || "")
      .replace(/\s+/g, " ")
      .trim()
      .toLocaleLowerCase();
  }

  function controlLabel(control) {
    if (!control) {
      return "";
    }

    return normalize(
      control.getAttribute?.("aria-label") ||
      control.textContent ||
      "");
  }

  function isSafeRetryControl(control) {
    return SAFE_RETRY_LABELS.has(controlLabel(control));
  }

  function isTransientSurface(element, classifyTransientError) {
    const text = String(element?.textContent || "");
    if (!text || text.length > MAX_SURFACE_TEXT_LENGTH) {
      return false;
    }
    return classifyTransientError(text);
  }

  function findLocalTransientAncestor(control, classifyTransientError) {
    let current = control?.parentElement || null;
    for (let depth = 0; current && depth < MAX_ANCESTOR_DEPTH; depth += 1) {
      if (isTransientSurface(current, classifyTransientError)) {
        return current;
      }
      current = current.parentElement || null;
    }
    return null;
  }

  function findSafeRecoveryCandidate(root, classifyTransientError) {
    if (!root?.querySelectorAll || typeof classifyTransientError !== "function") {
      return null;
    }

    const controls = root.querySelectorAll("button, [role='button']");
    for (const control of controls) {
      if (!isSafeRetryControl(control)) {
        continue;
      }
      const surface = findLocalTransientAncestor(control, classifyTransientError);
      if (surface) {
        return { control, surface };
      }
    }
    return null;
  }

  function findTransientErrorSurface(root, classifyTransientError) {
    if (!root?.querySelectorAll || typeof classifyTransientError !== "function") {
      return null;
    }

    const surfaces = root.querySelectorAll(
      "[role='alert'], [role='status'], [data-testid*='error']");
    for (const surface of surfaces) {
      if (isTransientSurface(surface, classifyTransientError)) {
        return surface;
      }
    }
    return null;
  }

  return {
    findSafeRecoveryCandidate,
    findTransientErrorSurface,
    isSafeRetryControl
  };
});
