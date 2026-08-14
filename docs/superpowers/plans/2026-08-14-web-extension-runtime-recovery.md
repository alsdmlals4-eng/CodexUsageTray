# Web Extension Runtime Recovery Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prevent a stale ChatGPT content script from repeatedly throwing when its Chrome/Edge extension runtime is invalidated, while preserving normal completion and approval notifications.

**Architecture:** Add a small testable runtime-message boundary between `content.js` and `chrome.runtime.sendMessage`. The content script owns its observer/timer lifecycle and shuts both down only when the boundary identifies an invalidated extension context; ordinary delivery failures remain non-fatal and keep monitoring active.

**Tech Stack:** Chrome/Edge Manifest V3, plain JavaScript, Node.js built-in `assert`/`vm`, Windows PowerShell 5.1 installer scripts, GitHub Actions, .NET 8 packaging.

## Global Constraints

- Do not add extension permissions or reload ChatGPT tabs automatically.
- Do not change ChatGPT DOM completion selectors, native messaging payloads, Codex Hooks, tray popup behavior, or usage collection.
- Preserve the stable extension ID derived from the existing manifest `key`.
- The stale script must stop producing errors; the user refreshes the ChatGPT tab to start a fresh script.
- The release target is app `v1.2.5` and extension manifest `1.1.1`.
- Keep browser code dependency-free and compatible with Chrome/Edge Manifest V3.

## File Structure

- Create `browser-extension/runtime-messaging.js`: isolated runtime availability, send, rejection, and invalidation handling.
- Create `tests/browser-extension/runtime-messaging.test.js`: unit contract for missing, throwing, rejecting, and healthy runtimes.
- Create `tests/browser-extension/content-runtime.test.js`: execute the real content script in a controlled VM and verify observer/timer shutdown.
- Create `tests/browser-extension/manifest.test.js`: protect script load order, extension version, and unchanged permission scope.
- Modify `browser-extension/content.js`: route sends through the boundary and own an idempotent monitoring shutdown.
- Modify `browser-extension/manifest.json`: load the new boundary before `content.js` and bump the extension version.
- Modify `.github/workflows/ci.yml` and `.github/workflows/release.yml`: syntax-check and run all new browser tests; package-smoke the new file.
- Modify `browser-extension/README.txt`, `README.md`, `scripts/setup-integration.ps1`, and `scripts/install-release.ps1`: explain that extension reload must be followed by ChatGPT tab refresh.
- Modify `.release-version`: publish `v1.2.5` only after the implementation and full validation are complete.

---

### Task 1: Runtime message boundary

**Files:**
- Create: `tests/browser-extension/runtime-messaging.test.js`
- Create: `browser-extension/runtime-messaging.js`

**Interfaces:**
- Consumes: a Chrome-like runtime object with optional `id` and `sendMessage(message)`.
- Produces: `CodexUsageTrayRuntimeMessaging.sendRuntimeMessage(runtime, message, onContextInvalidated): boolean` and `isContextInvalidated(error): boolean`.

- [ ] **Step 1: Write the failing runtime boundary test**

Create `tests/browser-extension/runtime-messaging.test.js` with literal cases for the user-observed missing runtime, a synchronous `Extension context invalidated` exception, an asynchronous ordinary delivery rejection, an asynchronous invalidation rejection, and a healthy send:

```javascript
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

  const throwingRuntime = {
    id: "extension-id",
    sendMessage() {
      throw new Error("Extension context invalidated.");
    }
  };
  assert.equal(sendRuntimeMessage(throwingRuntime, {}, () => invalidations += 1), false);
  assert.equal(invalidations, 2);

  const ordinaryRejection = {
    id: "extension-id",
    sendMessage() {
      return Promise.reject(new Error("Receiving end does not exist."));
    }
  };
  assert.equal(sendRuntimeMessage(ordinaryRejection, {}, () => invalidations += 1), true);
  await Promise.resolve();
  assert.equal(invalidations, 2);

  const invalidatedRejection = {
    id: "extension-id",
    sendMessage() {
      return Promise.reject(new Error("Extension context invalidated."));
    }
  };
  assert.equal(sendRuntimeMessage(invalidatedRejection, {}, () => invalidations += 1), true);
  await Promise.resolve();
  assert.equal(invalidations, 3);

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
  assert.equal(invalidations, 3);

  console.log("runtime messaging regression tests passed");
})().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
```

- [ ] **Step 2: Run the test and verify RED**

Run:

```bash
node tests/browser-extension/runtime-messaging.test.js
```

Expected: FAIL with `Cannot find module '../../browser-extension/runtime-messaging.js'`.

- [ ] **Step 3: Implement the minimal runtime boundary**

Create `browser-extension/runtime-messaging.js` using the same browser-global/CommonJS exposure pattern as `completion-state.js`:

```javascript
"use strict";

(function exposeRuntimeMessaging(root, factory) {
  const api = factory();
  root.CodexUsageTrayRuntimeMessaging = api;
  if (typeof module === "object" && module.exports) {
    module.exports = api;
  }
})(globalThis, () => {
  function isContextInvalidated(error) {
    const message = String(error?.message || error || "");
    return message.toLocaleLowerCase().includes("extension context invalidated");
  }

  function sendRuntimeMessage(runtime, message, onContextInvalidated = () => {}) {
    if (!runtime?.id || typeof runtime.sendMessage !== "function") {
      onContextInvalidated();
      return false;
    }

    let pending;
    try {
      pending = runtime.sendMessage(message);
    }
    catch (error) {
      if (isContextInvalidated(error)) {
        onContextInvalidated();
      }
      return false;
    }

    if (pending && typeof pending.catch === "function") {
      pending.catch((error) => {
        if (isContextInvalidated(error)) {
          onContextInvalidated();
        }
      });
    }
    return true;
  }

  return { isContextInvalidated, sendRuntimeMessage };
});
```

- [ ] **Step 4: Run the focused test and verify GREEN**

Run:

```bash
node --check browser-extension/runtime-messaging.js
node tests/browser-extension/runtime-messaging.test.js
```

Expected: both exit `0`; the test prints `runtime messaging regression tests passed`.

- [ ] **Step 5: Commit the runtime boundary**

```bash
git add browser-extension/runtime-messaging.js tests/browser-extension/runtime-messaging.test.js
git commit -m "fix: guard invalidated browser runtime"
```

### Task 2: Content monitoring lifecycle and manifest wiring

**Files:**
- Create: `tests/browser-extension/content-runtime.test.js`
- Create: `tests/browser-extension/manifest.test.js`
- Modify: `browser-extension/content.js`
- Modify: `browser-extension/manifest.json`
- Modify: `.github/workflows/ci.yml`
- Modify: `.github/workflows/release.yml`

**Interfaces:**
- Consumes: `CodexUsageTrayRuntimeMessaging.sendRuntimeMessage(runtime, message, stopMonitoring)` from Task 1.
- Produces: idempotent `stopMonitoring()` inside `content.js`; manifest content scripts ordered as `completion-state.js`, `runtime-messaging.js`, `content.js`.

- [ ] **Step 1: Write the failing content lifecycle test**

Create `tests/browser-extension/content-runtime.test.js`. Load the actual `content.js` with Node `vm`, supply `chrome: {}` and a completion state that emits once, and assert no exception, one observer disconnect, and one interval clear:

```javascript
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
console.log("content runtime invalidation regression test passed");
```

- [ ] **Step 2: Run the content test and verify RED**

Run:

```bash
node tests/browser-extension/content-runtime.test.js
```

Expected: FAIL with the user-observed `Cannot read properties of undefined (reading 'sendMessage')`, or fail the disconnect/interval assertions because monitoring is not stopped.

- [ ] **Step 3: Add idempotent shutdown and route sends through the boundary**

Modify `browser-extension/content.js` so it keeps lifecycle state and never calls `chrome.runtime` directly:

```javascript
let monitoringStopped = false;
let monitoringIntervalId = null;

function stopMonitoring() {
  if (monitoringStopped) {
    return;
  }

  monitoringStopped = true;
  observer.disconnect();
  if (monitoringIntervalId !== null) {
    clearInterval(monitoringIntervalId);
    monitoringIntervalId = null;
  }
}
```

Guard `sendActivity`, `inspectPage`, and the scheduled observer callback with `monitoringStopped`. Replace the direct send with:

```javascript
CodexUsageTrayRuntimeMessaging.sendRuntimeMessage(
  globalThis.chrome?.runtime,
  {
    type: "codex-usage-tray-activity",
    activity: { status, activityId, url, title: getSafeTitle() }
  },
  stopMonitoring);
```

Assign the interval before the initial inspection:

```javascript
monitoringIntervalId = setInterval(() => inspectPage(false), 500);
inspectPage(false);
```

- [ ] **Step 4: Wire and test the manifest contract**

Modify `browser-extension/manifest.json`:

```json
"version": "1.1.1",
"js": [
  "completion-state.js",
  "runtime-messaging.js",
  "content.js"
]
```

Create `tests/browser-extension/manifest.test.js` to assert the literal version, script order, `nativeMessaging` permission, and sole `https://chatgpt.com/*` host permission.

- [ ] **Step 5: Update CI and release workflow browser checks**

In both workflows, add:

```powershell
node --check .\browser-extension\runtime-messaging.js
node .\tests\browser-extension\runtime-messaging.test.js
node .\tests\browser-extension\content-runtime.test.js
node .\tests\browser-extension\manifest.test.js
```

In `.github/workflows/release.yml`, add `browser-extension\runtime-messaging.js` to the smoke-install required file list.

- [ ] **Step 6: Run browser tests and verify GREEN**

Run:

```bash
node --check browser-extension/background.js
node --check browser-extension/completion-state.js
node --check browser-extension/runtime-messaging.js
node --check browser-extension/content.js
node --check browser-extension/tab-focus.js
node tests/browser-extension/completion-state.test.js
node tests/browser-extension/runtime-messaging.test.js
node tests/browser-extension/content-runtime.test.js
node tests/browser-extension/manifest.test.js
node tests/browser-extension/tab-focus.test.js
```

Expected: all commands exit `0`; existing completion-state and tab-focus counts remain 3 and 4.

- [ ] **Step 7: Commit lifecycle and wiring changes**

```bash
git add browser-extension/content.js browser-extension/manifest.json tests/browser-extension/content-runtime.test.js tests/browser-extension/manifest.test.js .github/workflows/ci.yml .github/workflows/release.yml
git commit -m "fix: stop stale ChatGPT extension monitoring"
```

### Task 3: Installation guidance and v1.2.5 release metadata

**Files:**
- Modify: `browser-extension/README.txt`
- Modify: `README.md`
- Modify: `scripts/setup-integration.ps1`
- Modify: `scripts/install-release.ps1`
- Modify: `.release-version`

**Interfaces:**
- Consumes: the manual recovery contract from Tasks 1-2.
- Produces: consistent user instructions and release trigger `v1.2.5`.

- [ ] **Step 1: Update user-facing recovery guidance**

Document these exact actions in the extension README and main troubleshooting section:

1. Reload the unpacked extension after an update.
2. Refresh every already-open `chatgpt.com` tab.
3. Clearing the extension error list alone does not restart a stale content script.

Update both installer messages to end with the ChatGPT-tab refresh instruction. Keep the existing folder path and extension-page guidance.

- [ ] **Step 2: Advance release metadata**

Set `.release-version` to:

```text
v1.2.5
```

Add a concise README note that v1.2.5 suppresses stale-context error loops and requires a one-time ChatGPT-tab refresh after extension reload.

- [ ] **Step 3: Run documentation and script integrity checks**

Run:

```bash
git diff --check
rg -n "v1\.2\.5|ChatGPT 탭|모두 삭제" README.md browser-extension/README.txt scripts/install-release.ps1 scripts/setup-integration.ps1 .release-version
```

Expected: no whitespace errors; all recovery and release markers appear in their intended files.

- [ ] **Step 4: Commit documentation and release metadata**

```bash
git add browser-extension/README.txt README.md scripts/setup-integration.ps1 scripts/install-release.ps1 .release-version
git commit -m "docs: prepare v1.2.5 web runtime recovery"
```

### Task 4: Full verification, adversarial review, PR, and release

**Files:**
- Verify all files changed by Tasks 1-3.
- No additional product files unless a verified failure requires a scoped correction.

**Interfaces:**
- Consumes: the complete v1.2.5 branch.
- Produces: merged PR, successful Windows CI, release ZIP and SHA-256 asset.

- [ ] **Step 1: Run local verification**

Run the complete browser command block from Task 2, then:

```bash
git diff --check origin/main...HEAD
git status --short --branch
```

Expected: all browser checks pass, no diff-check errors, and only intentional committed changes remain.

- [ ] **Step 2: Perform adversarial regression review**

Review the diff against these attacks:

- `chrome` exists but `runtime` is absent.
- `runtime.id` disappears after extension reload.
- `sendMessage` throws synchronously.
- `sendMessage` rejects asynchronously for invalidation.
- `sendMessage` rejects for a temporary receiver failure and monitoring must continue.
- invalidation callback fires twice and observer/interval stop only once.
- manifest loads the boundary after `content.js`.
- new extension permission or host scope is introduced.
- normal completion and approval payload shape changes.

For every attack, cite the exact automated test or diff line that prevents it. Add only a focused failing test if an uncovered case is found.

- [ ] **Step 3: Publish a draft PR and wait for Windows CI**

Push `agent/web-runtime-guard-v125`, open a draft PR targeting `main`, and wait for the `Test Windows app` workflow. Inspect every job and log; do not infer Windows success from local Node tests.

- [ ] **Step 4: Merge only after all required checks pass**

Mark the PR ready, merge it, and verify `origin/main` contains the merge commit. The `.release-version` change should trigger `Release Windows app`.

- [ ] **Step 5: Verify release assets independently**

Confirm release `v1.2.5` contains:

- `CodexUsageTray-win-x64.zip`
- `CodexUsageTray-win-x64.zip.sha256`

Download both, compute SHA-256 locally, compare it with the published checksum, extract the archive, and confirm `browser-extension/runtime-messaging.js` plus manifest version `1.1.1` are present.

- [ ] **Step 6: User acceptance procedure**

Provide these exact steps:

```powershell
irm https://raw.githubusercontent.com/alsdmlals4-eng/CodexUsageTray/main/install-online.ps1 | iex
```

Then reload the unpacked extension in `chrome://extensions` or `edge://extensions`, refresh open ChatGPT tabs, request a short answer, verify the top completion popup, click it, and verify focus returns to the existing source chat.
