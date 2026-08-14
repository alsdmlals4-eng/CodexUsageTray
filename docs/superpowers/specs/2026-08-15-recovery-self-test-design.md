# Safe Recovery Self-Test Design

## Goal

Add a tray-menu recovery self-test that validates the production recovery policy and Windows recovery-status UI without waiting for a real ChatGPT failure and without modifying the ChatGPT page, disconnecting the network, reloading a browser tab, or resending a prompt.

## Chosen approach

Use an in-process `RecoverySelfTestRunner` that exercises the existing `BrowserRecoveryCoordinator` with synthetic ChatGPT recovery events and a no-side-effect attempt executor. After the policy checks pass, `TrayApplicationContext` emits a test-only `RecoveryRequired` through the existing `HandleActivity` path. When the user acknowledges that popup, the existing popup callback transitions the same synthetic activity to `Recovered` and routes that through `HandleActivity` as well.

This is preferred over two rejected alternatives:

1. Injecting fake error DOM into ChatGPT would test more of the extension detector but would mutate a live page and could produce misleading production behavior.
2. Adding a browser-extension debug protocol would increase cross-component surface area and create a debug transport that is unnecessary for validating the coordinator and tray UI.

## User-visible behavior

The tray context menu gains one item:

`복구 기능 테스트`

When clicked:

1. The self-test verifies attempt 1 is scheduled at 3 seconds.
2. A duplicate signal while an attempt is pending is suppressed.
3. After completion, attempt 2 is scheduled at 10 seconds.
4. After completion, attempt 3 is scheduled at 30 seconds.
5. A fourth attempt is refused.
6. A `Recovered` event resets the isolated coordinator state.
7. A new disconnected signal after reset starts again at attempt 1 / 3 seconds.
8. If all checks pass, the tray emits a synthetic `[테스트] 복구 필요` event through the normal activity store and popup queue.
9. The synthetic activity remains `RecoveryRequired` until the user clicks that popup.
10. Acknowledging the first popup transitions the same activity key to `Recovered` and shows the normal `[테스트] 자동 복구` popup.
11. Acknowledging the recovered popup completes the self-test and releases the re-entrancy guard.

The acknowledgement boundary is intentional. `ActivityPopupQueue` already coalesces a new status with the same `ActivityKey` into the currently visible popup. Emitting `RecoveryRequired` and `Recovered` back-to-back would therefore skip the visible `RecoveryRequired` stage. The self-test preserves that existing queue contract rather than changing global popup behavior.

## Safety boundaries

- No ChatGPT DOM mutation.
- No browser tab reload or navigation.
- No browser activation command is required for this test.
- No prompt or message is sent to ChatGPT.
- No network connection is intentionally interrupted.
- No OpenAI API request is made.
- No approval/confirmation action is generated.
- No new browser permission or Native Messaging command is added.
- The synthetic UI event detail is exactly `recovery_self_test`, never `disconnected_waiting`.
- `TrayApplicationContext.HandleActivity` explicitly stops self-test events after activity-store/popup processing. They are not passed to `MobileNotificationRuntime` or the production `_browserRecovery` / `BrowserActivityActivator` path.
- The synthetic UI activity carries no `SourceUri`, BrowserConnectionId, tab ID, or window ID.
- The self-test runner uses a fresh `BrowserRecoveryCoordinator`, so it cannot consume or reset a real conversation's recovery attempts.
- Repeated menu clicks are ignored until the test reaches the acknowledged `Recovered` state.

## Components

### `RecoverySelfTestRunner`

Lives in `CodexUsageTray.Core` and owns deterministic policy verification. It creates isolated synthetic `ChatGptWeb` activity events and exercises `BrowserRecoveryCoordinator.Plan()` / `MarkAttemptCompleted()` without sleeping for 43 seconds.

It returns a structured `RecoverySelfTestResult` containing pass/fail state and the verified policy evidence. The runner fails closed: an unexpected instruction, wrong delay, failed attempt executor, missing suppression, missing ceiling, or missing reset produces a failed result rather than reporting success.

The runner may use `disconnected_waiting` internally because it is deliberately testing the real coordinator scheduling policy, but that event never enters `TrayApplicationContext`, carries no browser identity, and uses a fresh coordinator instance.

### `TrayApplicationContext`

Adds the `복구 기능 테스트` menu item and owns the UI self-test lifecycle:

- run `RecoverySelfTestRunner` with a no-side-effect executor;
- on runner failure, show an error dialog and emit no recovery-success claim;
- on success, emit only test `RecoveryRequired` with detail `recovery_self_test`;
- intercept acknowledgement of that test popup in `OpenActivity` and transition the same activity to `Recovered`;
- acknowledge the recovered popup to finish the test and clear the re-entrancy guard;
- explicitly prevent self-test events from reaching mobile push or production browser recovery fan-out.

## Error handling

- A self-test failure never changes production recovery state.
- A failed self-test does not restart the tray or browser.
- Repeated clicks while one test is active are ignored.
- The result text identifies itself as a test so it cannot be confused with a real ChatGPT outage.
- Global `ActivityPopupQueue` coalescing semantics are unchanged.

## Test contract

### Core tests

Verify `RecoverySelfTestRunner` proves all of the following against the real coordinator implementation:

- delays exactly `3s, 10s, 30s`;
- pending duplicate suppression;
- maximum three attempts;
- `Recovered` resets state;
- post-reset next instruction is attempt 1 at 3 seconds;
- executor failure returns a failed self-test result.

### Windows UI tests

Verify:

- tray menu exposes `복구 기능 테스트`;
- clicking it produces a test `RecoveryRequired` popup through the normal queue;
- the activity store remains `RecoveryRequired` before acknowledgement;
- the synthetic activity has no browser navigation identity;
- a fresh production coordinator would not schedule a reload for detail `recovery_self_test`;
- clicking the first popup transitions the same activity key to `Recovered` and displays the normal recovered popup;
- clicking the recovered popup drains the test popup sequence;
- final activity-store state is `Recovered`, not a fake unresolved warning.

### Full regression

Require the existing Windows PR CI to remain green: browser extension validation, PowerShell installer tests, desktop shortcut tests, core/recovery/RecoveryRunner tests, Windows UI/mobile notification regressions, full solution build, and EventBridge integration.

## Remaining live-validation boundary

This self-test does not prove that a future ChatGPT DOM revision will still expose a real timeout/disconnection in a form the extension detector recognizes. That final detector boundary remains naturally validated when an actual transient ChatGPT error occurs. The test intentionally avoids manufacturing a live DOM failure because safety and false-positive avoidance take priority over artificially forcing that last boundary.
