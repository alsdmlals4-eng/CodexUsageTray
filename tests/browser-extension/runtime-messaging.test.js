"use strict";

const assert = require("node:assert/strict");
const {
  isContextInvalidated,
  sendRuntimeMessage
} = require("../../browser-extension/runtime-messaging.js");

(async () => {
  assert.equal(isContextInvalidated(new Error("Extension context invalidated.")), true);
  assert.equal(isContextInvalidated(new Error("Receiving end does not exist.")), false);

  let invalidations = 0;
  assert.equal(sendRuntimeMessage(undefined, { type: "activity" }, () => invalidations += 1), false);
  assert.equal(invalidations, 1);

  let idlessCalls = 0;
  const idlessRuntime = {
    sendMessage() {
      idlessCalls += 1;
    }
  };
  assert.equal(sendRuntimeMessage(idlessRuntime, {}, () => invalidations += 1), false);
  assert.equal(idlessCalls, 0);
  assert.equal(invalidations, 2);

  const throwingRuntime = {
    id: "extension-id",
    sendMessage() {
      throw new Error("Extension context invalidated.");
    }
  };
  assert.equal(sendRuntimeMessage(throwingRuntime, {}, () => invalidations += 1), false);
  assert.equal(invalidations, 3);

  const ordinaryThrow = {
    id: "extension-id",
    sendMessage() {
      throw new Error("Unexpected native messaging error.");
    }
  };
  assert.equal(sendRuntimeMessage(ordinaryThrow, {}, () => invalidations += 1), false);
  assert.equal(invalidations, 3);

  const ordinaryRejection = {
    id: "extension-id",
    sendMessage() {
      return Promise.reject(new Error("Receiving end does not exist."));
    }
  };
  assert.equal(sendRuntimeMessage(ordinaryRejection, {}, () => invalidations += 1), true);
  await Promise.resolve();
  assert.equal(invalidations, 3);

  const invalidatedRejection = {
    id: "extension-id",
    sendMessage() {
      return Promise.reject(new Error("Extension context invalidated."));
    }
  };
  assert.equal(sendRuntimeMessage(invalidatedRejection, {}, () => invalidations += 1), true);
  await Promise.resolve();
  assert.equal(invalidations, 4);

  const sent = [];
  const healthyRuntime = {
    id: "extension-id",
    sendMessage(message) {
      sent.push(message);
      return Promise.resolve({ ok: true });
    }
  };
  const message = { type: "codex-usage-tray-activity" };
  assert.equal(sendRuntimeMessage(healthyRuntime, message, () => invalidations += 1), true);
  await Promise.resolve();
  assert.deepEqual(sent, [message]);
  assert.equal(invalidations, 4);

  console.log("7 runtime messaging regression tests passed");
})().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
