"use strict";

const assert = require("node:assert/strict");
const { CompletionState } = require("../../browser-extension/completion-state.js");

function observe(state, now, generating, assistantMutated = false, routeKey = "/c/thread-1") {
  return state.observe({ now, generating, assistantMutated, routeKey });
}

{
  const state = new CompletionState(2000);
  assert.equal(observe(state, 0, true).completed, false);
  assert.equal(observe(state, 100, false).completed, false);
  assert.equal(observe(state, 900, true).completed, false,
    "a transient Stop-button disappearance at answer start must be cancelled");
  assert.equal(observe(state, 1000, false).completed, false);
  assert.equal(observe(state, 3100, false).completed, true,
    "stable Stop-button absence must complete after the grace period");
  assert.equal(observe(state, 5000, false).completed, false,
    "one generation must emit at most one completion");
}

{
  const state = new CompletionState(2000);
  assert.equal(observe(state, 0, true).completed, false);
  assert.equal(observe(state, 100, false).completed, false);
  assert.equal(observe(state, 1900, false, true).completed, false,
    "assistant DOM activity must postpone completion");
  assert.equal(observe(state, 3800, false).completed, false);
  assert.equal(observe(state, 4000, false).completed, true,
    "completion requires two seconds without assistant DOM changes");
}

{
  const state = new CompletionState(2000);
  assert.equal(observe(state, 0, true).completed, false);
  assert.equal(observe(state, 100, false).completed, false);
  assert.equal(observe(state, 2200, false, false, "/c/thread-2").completed, false,
    "route changes must discard the previous conversation candidate");
}

console.log("3 completion-state regression tests passed");
