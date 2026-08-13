"use strict";

const assert = require("node:assert/strict");
const { createActivationPlan } = require("../../browser-extension/tab-focus.js");

const target = "https://chatgpt.com/c/thread-1";
const tabs = [
  { id: 17, windowId: 3, url: "https://chatgpt.com/c/thread-1?model=gpt-5" },
  { id: 21, windowId: 8, url: "https://chatgpt.com/c/thread-1#bottom" },
  { id: 44, windowId: 8, url: "https://chatgpt.com/c/other-thread" }
];

assert.deepEqual(
  createActivationPlan(tabs, target, 21),
  { action: "focus", tabId: 21, windowId: 8 },
  "the exact preferred source tab must win");

assert.deepEqual(
  createActivationPlan(tabs, target, 999),
  { action: "focus", tabId: 17, windowId: 3 },
  "another existing tab with the exact normalized URL must be reused");

assert.deepEqual(
  createActivationPlan(tabs, "https://chatgpt.com/c/missing", 999),
  { action: "create", url: "https://chatgpt.com/c/missing" },
  "a new tab is allowed only when no exact conversation tab exists");

assert.throws(
  () => createActivationPlan(tabs, "https://example.com/c/thread-1", 17),
  /ChatGPT/,
  "non-ChatGPT navigation targets must be rejected");

console.log("4 tab-focus regression tests passed");
