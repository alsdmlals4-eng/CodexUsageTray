"use strict";

const assert = require("node:assert/strict");
const manifest = require("../../browser-extension/manifest.json");

assert.equal(manifest.manifest_version, 3);
assert.equal(manifest.version, "1.2.0");
assert.deepEqual(manifest.permissions, ["nativeMessaging"]);
assert.deepEqual(manifest.host_permissions, ["https://chatgpt.com/*"]);
assert.deepEqual(manifest.content_scripts[0].js, [
  "completion-state.js",
  "recovery-watchdog.js",
  "recovery-dom.js",
  "runtime-messaging.js",
  "content.js"
]);
console.log("5 browser manifest contract assertions passed");
