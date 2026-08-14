"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const manifest = require("../../browser-extension/manifest.json");
const releaseVersion = fs.readFileSync(
  path.join(__dirname, "../../.release-version"),
  "utf8"
).trim();

assert.equal(manifest.manifest_version, 3);
assert.equal(`v${manifest.version}`, releaseVersion);
assert.deepEqual(manifest.permissions, ["nativeMessaging"]);
assert.deepEqual(manifest.host_permissions, ["https://chatgpt.com/*"]);
assert.deepEqual(manifest.content_scripts[0].js, [
  "completion-state.js",
  "recovery-watchdog.js",
  "recovery-dom.js",
  "recovery-action-controller.js",
  "runtime-messaging.js",
  "content.js"
]);
console.log("5 browser manifest contract assertions passed");
