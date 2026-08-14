"use strict";

const assert = require("node:assert/strict");
const { RecoveryActionController } = require("../../browser-extension/recovery-action-controller.js");

const scheduled = [];
const events = [];
const controller = new RecoveryActionController({
  schedule(callback, delayMilliseconds) {
    scheduled.push({ callback, delayMilliseconds });
    return scheduled.length;
  },
  sendActivity(activity) {
    events.push(activity);
  }
});

let routeKey = "https://chatgpt.com/c/thread-1";
let clicks = 0;
const retryControl = { click() { clicks += 1; } };
const currentCandidate = () => ({
  routeKey,
  candidate: { control: retryControl }
});

assert.equal(controller.requestRetry({
  routeKey,
  attempt: 1,
  delayMilliseconds: 3000,
  getCurrentCandidate: currentCandidate
}), true);
assert.equal(controller.requestRetry({
  routeKey,
  attempt: 1,
  delayMilliseconds: 3000,
  getCurrentCandidate: currentCandidate
}), false, "only one retry timer may be pending per route");
assert.equal(scheduled.length, 1);
assert.equal(scheduled[0].delayMilliseconds, 3000);
assert.deepEqual(events, [{ status: "retrying", reason: "transient_error", attempt: 1 }]);

scheduled[0].callback();
assert.equal(clicks, 1, "the still-safe retry control is clicked once");

assert.equal(controller.requestRetry({
  routeKey,
  attempt: 2,
  delayMilliseconds: 10000,
  getCurrentCandidate: currentCandidate
}), true);
routeKey = "https://chatgpt.com/c/thread-2";
scheduled[1].callback();
assert.equal(clicks, 1, "a route change must cancel the stale retry click");
assert.deepEqual(events.at(-1), {
  status: "recovery_required",
  reason: "route_changed",
  attempt: 2
});

controller.reportRecoveryRequired({
  routeKey,
  reason: "stalled"
});
controller.reportRecoveryRequired({
  routeKey,
  reason: "stalled"
});
assert.equal(
  events.filter((item) => item.status === "recovery_required" && item.reason === "stalled").length,
  1,
  "identical recovery-required states must be deduplicated");

controller.reportRecovered({ routeKey, reason: "generation_resumed" });
assert.deepEqual(events.at(-1), {
  status: "recovered",
  reason: "generation_resumed"
});
controller.reportRecoveryRequired({ routeKey, reason: "stalled" });
assert.equal(
  events.filter((item) => item.status === "recovery_required" && item.reason === "stalled").length,
  2,
  "a recovered route may report a new later interruption");

console.log("PASS recovery action controller contracts");
