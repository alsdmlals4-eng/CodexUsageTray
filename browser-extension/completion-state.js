"use strict";

(function exposeCompletionState(root, factory) {
  const api = factory();
  root.CodexUsageTrayCompletionState = api;
  if (typeof module === "object" && module.exports) {
    module.exports = api;
  }
})(globalThis, () => {
  class CompletionState {
    constructor(stableMilliseconds = 2000) {
      if (!Number.isFinite(stableMilliseconds) || stableMilliseconds <= 0) {
        throw new RangeError("stableMilliseconds must be positive");
      }

      this.stableMilliseconds = stableMilliseconds;
      this.routeKey = null;
      this.running = false;
      this.candidateSince = null;
      this.lastAssistantMutationAt = null;
    }

    observe({ now, generating, assistantMutated, routeKey }) {
      if (!Number.isFinite(now)) {
        throw new TypeError("now must be a finite number");
      }

      if (this.routeKey !== routeKey) {
        this.routeKey = routeKey;
        this.running = false;
        this.candidateSince = null;
        this.lastAssistantMutationAt = null;
      }

      if (generating) {
        this.running = true;
        this.candidateSince = null;
        if (assistantMutated) {
          this.lastAssistantMutationAt = now;
        }
        return { completed: false };
      }

      if (!this.running) {
        return { completed: false };
      }

      if (assistantMutated) {
        this.lastAssistantMutationAt = now;
        this.candidateSince = now;
        return { completed: false };
      }

      if (this.candidateSince === null) {
        this.candidateSince = now;
      }

      const stableSince = Math.max(
        this.candidateSince,
        this.lastAssistantMutationAt ?? this.candidateSince);
      if (now - stableSince < this.stableMilliseconds) {
        return { completed: false };
      }

      this.running = false;
      this.candidateSince = null;
      this.lastAssistantMutationAt = null;
      return { completed: true };
    }
  }

  return { CompletionState };
});
