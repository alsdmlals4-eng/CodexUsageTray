"use strict";

(function exposeRecoveryActionController(root, factory) {
  const api = factory();
  root.CodexUsageTrayRecoveryActionController = api;
  if (typeof module === "object" && module.exports) {
    module.exports = api;
  }
})(globalThis, () => {
  class RecoveryActionController {
    constructor({ schedule = setTimeout, sendActivity } = {}) {
      if (typeof schedule !== "function") {
        throw new TypeError("schedule must be a function");
      }
      if (typeof sendActivity !== "function") {
        throw new TypeError("sendActivity must be a function");
      }

      this.schedule = schedule;
      this.sendActivity = sendActivity;
      this.pendingRetry = null;
      this.lastRequiredKey = null;
    }

    requestRetry({
      routeKey,
      attempt,
      delayMilliseconds,
      getCurrentCandidate
    }) {
      if (!routeKey || !Number.isInteger(attempt) || attempt <= 0 ||
          !Number.isFinite(delayMilliseconds) || delayMilliseconds < 0 ||
          typeof getCurrentCandidate !== "function") {
        throw new TypeError("invalid retry request");
      }

      if (this.pendingRetry?.routeKey === routeKey) {
        return false;
      }

      const pending = { routeKey, attempt };
      this.pendingRetry = pending;
      this.sendActivity({
        status: "retrying",
        reason: "transient_error",
        attempt
      });

      this.schedule(() => {
        if (this.pendingRetry !== pending) {
          return;
        }
        this.pendingRetry = null;

        const current = getCurrentCandidate();
        if (!current || current.routeKey !== routeKey) {
          this._reportRequired(routeKey, "route_changed", attempt);
          return;
        }

        const control = current.candidate?.control;
        if (typeof control?.click !== "function") {
          this._reportRequired(routeKey, "retry_surface_changed", attempt);
          return;
        }

        control.click();
      }, delayMilliseconds);
      return true;
    }

    reportRecoveryRequired({ routeKey, reason, attempt }) {
      if (!routeKey || !reason) {
        throw new TypeError("routeKey and reason are required");
      }
      return this._reportRequired(routeKey, reason, attempt);
    }

    reportRecovered({ routeKey, reason }) {
      if (!routeKey || !reason) {
        throw new TypeError("routeKey and reason are required");
      }
      this.lastRequiredKey = null;
      this.sendActivity({ status: "recovered", reason });
    }

    _reportRequired(routeKey, reason, attempt) {
      const key = `${routeKey}\u001f${reason}`;
      if (this.lastRequiredKey === key) {
        return false;
      }
      this.lastRequiredKey = key;
      const activity = { status: "recovery_required", reason };
      if (Number.isInteger(attempt) && attempt > 0) {
        activity.attempt = attempt;
      }
      this.sendActivity(activity);
      return true;
    }
  }

  return { RecoveryActionController };
});
