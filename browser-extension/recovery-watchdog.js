"use strict";

(function exposeRecoveryWatchdog(root, factory) {
  const api = factory();
  root.CodexUsageTrayRecoveryWatchdog = api;
  if (typeof module === "object" && module.exports) {
    module.exports = api;
  }
})(globalThis, () => {
  const RETRY_DELAYS = [3000, 10000, 30000];
  const TRANSIENT_ERROR_MARKERS = [
    "메시지 전송 시간이 초과되었습니다",
    "message timed out",
    "network error",
    "something went wrong",
    "error generating a response",
    "error while generating a response"
  ];

  function normalize(value) {
    return String(value || "")
      .replace(/\s+/g, " ")
      .trim()
      .toLocaleLowerCase();
  }

  function classifyTransientError(text) {
    const normalized = normalize(text);
    return normalized.length > 0 &&
      TRANSIENT_ERROR_MARKERS.some((marker) => normalized.includes(marker));
  }

  function getRetryDelay(attempt) {
    if (!Number.isInteger(attempt) || attempt < 1 || attempt > RETRY_DELAYS.length) {
      return null;
    }

    return RETRY_DELAYS[attempt - 1];
  }

  class RecoveryWatchdog {
    constructor({ stallMilliseconds = 180000, maxAttempts = 3 } = {}) {
      if (!Number.isFinite(stallMilliseconds) || stallMilliseconds <= 0) {
        throw new RangeError("stallMilliseconds must be positive");
      }
      if (!Number.isInteger(maxAttempts) || maxAttempts < 1 || maxAttempts > RETRY_DELAYS.length) {
        throw new RangeError("maxAttempts must be between 1 and 3");
      }

      this.stallMilliseconds = stallMilliseconds;
      this.maxAttempts = maxAttempts;
      this.routeKey = null;
      this.attempts = 0;
      this.lastAssistantMutationAt = null;
      this.generatingSince = null;
      this.stallReported = false;
    }

    observe({
      now,
      routeKey,
      generating,
      assistantMutated,
      transientError,
      hasSafeRetryControl
    }) {
      if (!Number.isFinite(now)) {
        throw new TypeError("now must be a finite number");
      }

      if (this.routeKey !== routeKey) {
        this.routeKey = routeKey;
        this.attempts = 0;
        this.lastAssistantMutationAt = null;
        this.generatingSince = null;
        this.stallReported = false;
      }

      if (assistantMutated) {
        this.lastAssistantMutationAt = now;
        this.stallReported = false;
      }

      if (generating) {
        if (this.generatingSince === null) {
          this.generatingSince = now;
        }

        const activityAt = this.lastAssistantMutationAt ?? this.generatingSince;
        if (!this.stallReported && now - activityAt >= this.stallMilliseconds) {
          this.stallReported = true;
          return { action: "recovery_required", reason: "stalled" };
        }
      }
      else {
        this.generatingSince = null;
      }

      if (!transientError) {
        return { action: "none" };
      }

      if (!hasSafeRetryControl) {
        return { action: "recovery_required", reason: "unsafe_or_missing_retry_control" };
      }

      if (this.attempts >= this.maxAttempts) {
        return { action: "recovery_required", reason: "retry_exhausted" };
      }

      const attempt = this.attempts + 1;
      const delayMilliseconds = getRetryDelay(attempt);
      if (delayMilliseconds === null) {
        return { action: "recovery_required", reason: "retry_exhausted" };
      }

      this.attempts = attempt;
      this.stallReported = false;
      return { action: "retry", attempt, delayMilliseconds };
    }
  }

  return {
    RecoveryWatchdog,
    classifyTransientError,
    getRetryDelay
  };
});
