"use strict";

const assert = require("node:assert/strict");
const {
  RecoveryWatchdog,
  classifyTransientError,
  getRetryDelay
} = require("../../browser-extension/recovery-watchdog.js");

assert.equal(classifyTransientError("메시지 전송 시간이 초과되었습니다. 다시 시도해 주세요"), true);
assert.equal(classifyTransientError("연결이 끊어졌습니다. 전체 답변을 기다리는 중입니다"), true);
assert.equal(classifyTransientError("Message timed out. Try again."), true);
assert.equal(classifyTransientError("A network error occurred"), true);
assert.equal(classifyTransientError("Something went wrong while generating a response"), true);
assert.equal(classifyTransientError("승인이 필요합니다"), false);
assert.equal(classifyTransientError("일반 대화 내용입니다"), false);

assert.equal(getRetryDelay(1), 3000);
assert.equal(getRetryDelay(2), 10000);
assert.equal(getRetryDelay(3), 30000);
assert.equal(getRetryDelay(4), null);

const watchdog = new RecoveryWatchdog({ stallMilliseconds: 180000, maxAttempts: 3 });
let result = watchdog.observe({
  now: 0,
  routeKey: "https://chatgpt.com/c/abc",
  generating: false,
  assistantMutated: false,
  transientError: true,
  hasSafeRetryControl: true
});
assert.deepEqual(result, { action: "retry", attempt: 1, delayMilliseconds: 3000 });

result = watchdog.observe({
  now: 4000,
  routeKey: "https://chatgpt.com/c/abc",
  generating: false,
  assistantMutated: false,
  transientError: true,
  hasSafeRetryControl: true
});
assert.deepEqual(result, { action: "retry", attempt: 2, delayMilliseconds: 10000 });

result = watchdog.observe({
  now: 15000,
  routeKey: "https://chatgpt.com/c/abc",
  generating: false,
  assistantMutated: false,
  transientError: true,
  hasSafeRetryControl: true
});
assert.deepEqual(result, { action: "retry", attempt: 3, delayMilliseconds: 30000 });

result = watchdog.observe({
  now: 46000,
  routeKey: "https://chatgpt.com/c/abc",
  generating: false,
  assistantMutated: false,
  transientError: true,
  hasSafeRetryControl: true
});
assert.deepEqual(result, { action: "recovery_required", reason: "retry_exhausted" });

const stalled = new RecoveryWatchdog({ stallMilliseconds: 180000, maxAttempts: 3 });
stalled.observe({
  now: 1000,
  routeKey: "https://chatgpt.com/c/stall",
  generating: true,
  assistantMutated: true,
  transientError: false,
  hasSafeRetryControl: false
});
result = stalled.observe({
  now: 181001,
  routeKey: "https://chatgpt.com/c/stall",
  generating: true,
  assistantMutated: false,
  transientError: false,
  hasSafeRetryControl: false
});
assert.deepEqual(result, { action: "recovery_required", reason: "stalled" });

const routeReset = new RecoveryWatchdog({ stallMilliseconds: 180000, maxAttempts: 3 });
routeReset.observe({
  now: 0,
  routeKey: "https://chatgpt.com/c/one",
  generating: false,
  assistantMutated: false,
  transientError: true,
  hasSafeRetryControl: true
});
result = routeReset.observe({
  now: 100,
  routeKey: "https://chatgpt.com/c/two",
  generating: false,
  assistantMutated: false,
  transientError: true,
  hasSafeRetryControl: true
});
assert.deepEqual(result, { action: "retry", attempt: 1, delayMilliseconds: 3000 });

console.log("PASS recovery watchdog contracts");
