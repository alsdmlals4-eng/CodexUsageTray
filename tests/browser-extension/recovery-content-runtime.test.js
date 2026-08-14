"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");

const source = fs.readFileSync(
  path.join(__dirname, "../../browser-extension/content.js"),
  "utf8");

function runScenario({ disconnected, safeCandidate, watchdogResult }) {
  const calls = {
    retries: [],
    required: [],
    recovered: []
  };
  const surface = { textContent: disconnected
    ? "연결이 끊어졌습니다. 전체 답변을 기다리는 중입니다"
    : "메시지 전송 시간이 초과되었습니다. 다시 시도해 주세요" };
  const candidate = safeCandidate
    ? { control: { click() {} }, surface }
    : null;

  class FakeRecoveryController {
    constructor() {}
    requestRetry(value) {
      calls.retries.push(value);
      return true;
    }
    reportRecoveryRequired(value) {
      calls.required.push(value);
      return true;
    }
    reportRecovered(value) {
      calls.recovered.push(value);
    }
  }

  const context = {
    URL,
    Date,
    Element: class Element {},
    location: { href: "https://chatgpt.com/c/recovery-thread", pathname: "/c/recovery-thread" },
    document: {
      title: "복구 테스트 | ChatGPT",
      documentElement: {},
      querySelector() { return null; },
      querySelectorAll() { return []; }
    },
    MutationObserver: class {
      constructor(callback) { this.callback = callback; }
      observe() {}
      disconnect() {}
    },
    setInterval() { return 1; },
    clearInterval() {},
    setTimeout(callback) { callback(); return 1; },
    CodexUsageTrayCompletionState: {
      CompletionState: class {
        observe() { return { completed: false }; }
      }
    },
    CodexUsageTrayRecoveryWatchdog: {
      RecoveryWatchdog: class {
        observe() { return watchdogResult; }
      },
      classifyTransientError() { return true; },
      isDisconnectedWaiting() { return disconnected; }
    },
    CodexUsageTrayRecoveryDom: {
      findSafeRecoveryCandidate() { return candidate; },
      findTransientErrorSurface() { return surface; }
    },
    CodexUsageTrayRecoveryActionController: {
      RecoveryActionController: FakeRecoveryController
    },
    CodexUsageTrayRuntimeMessaging: {
      sendRuntimeMessage() { return Promise.resolve(true); }
    },
    chrome: { runtime: {} },
    globalThis: null
  };
  context.globalThis = context;
  vm.createContext(context);
  vm.runInContext(source, context, { filename: "content.js" });
  return calls;
}

let calls = runScenario({
  disconnected: false,
  safeCandidate: true,
  watchdogResult: { action: "retry", attempt: 1, delayMilliseconds: 3000 }
});
assert.equal(calls.retries.length, 1, "a transient error must request one bounded retry");
assert.equal(calls.retries[0].attempt, 1);
assert.equal(calls.retries[0].delayMilliseconds, 3000);
assert.equal(calls.retries[0].routeKey, "https://chatgpt.com/c/recovery-thread");
assert.equal(typeof calls.retries[0].getCurrentCandidate, "function");

calls = runScenario({
  disconnected: true,
  safeCandidate: false,
  watchdogResult: { action: "recovery_required", reason: "unsafe_or_missing_retry_control" }
});
assert.deepEqual(JSON.parse(JSON.stringify(calls.required)), [{
  routeKey: "https://chatgpt.com/c/recovery-thread",
  reason: "disconnected_waiting"
}], "a disconnected waiting state without a safe button must route to reconnect recovery");

console.log("PASS recovery content runtime contracts");
