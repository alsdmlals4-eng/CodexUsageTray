"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");
const runtimeMessaging = require("../../browser-extension/runtime-messaging.js");

let disconnects = 0;
const clearedIntervals = [];

class FakeMutationObserver {
  observe() {}

  disconnect() {
    disconnects += 1;
  }
}

class CompletingState {
  observe() {
    return { completed: true };
  }
}

class FakeElement {}

const context = {
  chrome: {},
  location: {
    href: "https://chatgpt.com/c/thread-1",
    pathname: "/c/thread-1"
  },
  URL,
  document: {
    title: "Runtime test | ChatGPT",
    documentElement: {},
    querySelector() { return null; },
    querySelectorAll() { return []; }
  },
  Element: FakeElement,
  MutationObserver: FakeMutationObserver,
  CodexUsageTrayCompletionState: { CompletionState: CompletingState },
  CodexUsageTrayRuntimeMessaging: runtimeMessaging,
  setInterval() { return 17; },
  clearInterval(id) { clearedIntervals.push(id); },
  setTimeout(callback) { callback(); return 23; }
};
context.globalThis = context;

const source = fs.readFileSync(
  path.join(__dirname, "../../browser-extension/content.js"),
  "utf8");

assert.doesNotThrow(() => vm.runInNewContext(source, context));
context.stopMonitoring();
context.stopMonitoring();
assert.equal(disconnects, 1);
assert.deepEqual(clearedIntervals, [17]);

const sentMessages = [];
const healthyContext = {
  chrome: {
    runtime: {
      id: "extension-id",
      sendMessage(message) {
        sentMessages.push(message);
        return Promise.resolve({ ok: true });
      }
    }
  },
  location: {
    href: "https://chatgpt.com/c/thread-2?temporary-chat=true#latest",
    pathname: "/c/thread-2"
  },
  URL,
  Date: { now() { return 12345; } },
  document: {
    title: "Healthy runtime | ChatGPT",
    documentElement: {},
    querySelector() { return null; },
    querySelectorAll() { return []; }
  },
  Element: FakeElement,
  MutationObserver: FakeMutationObserver,
  CodexUsageTrayCompletionState: { CompletionState: CompletingState },
  CodexUsageTrayRuntimeMessaging: runtimeMessaging,
  setInterval() { return 18; },
  clearInterval() {},
  setTimeout(callback) { callback(); return 24; }
};
healthyContext.globalThis = healthyContext;

vm.runInNewContext(source, healthyContext);
assert.equal(sentMessages.length, 1);
assert.deepEqual(JSON.parse(JSON.stringify(sentMessages[0])), {
  type: "codex-usage-tray-activity",
  activity: {
    status: "completed",
    activityId: "complete-12345-1",
    url: "https://chatgpt.com/c/thread-2",
    title: "Healthy runtime"
  }
});

console.log("2 content runtime regression tests passed");
