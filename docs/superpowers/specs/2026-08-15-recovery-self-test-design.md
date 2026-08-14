# Safe Recovery Self-Test Design

## Goal

Add a tray-menu recovery self-test that validates the production recovery policy and Windows recovery-status UI without waiting for a real ChatGPT failure and without modifying the ChatGPT page, disconnecting the network, reloading a browser tab, or resending a prompt.

## Chosen approach

Use an in-process `RecoverySelfTestRunner` that exercises the existing `BrowserRecoveryCoordinator` with synthetic ChatGPT recovery events and a no-side-effect attempt executor. After the policy checks pass, `TrayApplicationContext` sends synthetic test-only `RecoveryRequired` and `Recovered` events through the existing `HandleActivity` path so the normal activity store and Windows popup queue are exercised.

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
6. A `Recovered` event resets the coordinator state.
7. A new disconnected signal after reset starts again at attempt 1 / 3 seconds.
8. If all checks pass, the tray emits a synthetic `[테스트] 복구 필요` event and then a synthetic `[테스트] 자동 복구` event through the existing activity handling path.

The first recovery popup remains subject to the existing popup queue behavior: the user clicks it before the queued recovered popup appears. The activity store uses the same synthetic activity key for both events, so the final history state is `Recovered` rather than leaving a fake unresolved recovery warning behind.

## Safety boundaries

- No ChatGPT DOM mutation.
- No browser tab reload or navigation.
- No browser activation command is required for this test.
- No prompt or message is sent to ChatGPT.
- No network connection is intentionally interrupted.
- No OpenAI API request is made.
- No approval/confirmation action is generated.
- No new browser permission or Native Messaging command is added.
- The synthetic event detail is `recovery_self_test`, not `disconnected_waiting`, so the production `BrowserRecoveryCoordinator` owned by `TrayApplicationContext` will not schedule a real reload when the UI events pass through `HandleActivity`.
- Mobile notification behavior remains unchanged; recovery states are not mobile-push eligible under the current policy.
- The self-test runner uses a fresh coordinator instance so it cannot consume or reset a real conversation's recovery attempts.

## Components

### `RecoverySelfTestRunner`

Lives in `CodexUsageTray.Core` and owns deterministic policy verification. It creates synthetic `ChatGptWeb` activity events and exercises `BrowserRecoveryCoordinator.Plan()` / `MarkAttemptCompleted()` without sleeping for 43 seconds.

It returns a structured `RecoverySelfTestResult` containing pass/fail state and the verified policy evidence. The runner must fail closed: an unexpected instruction, wrong delay, failed attempt executor, missing suppression, missing ceiling, or missing reset produces a failed result rather than reporting success.

### `TrayApplicationContext`

Adds the `복구 기능 테스트` menu item and a re-entrancy guard so repeated clicks cannot overlap tests. It invokes `RecoverySelfTestRunner` synchronously/quickly, then:

- on success: emits test-only `RecoveryRequired` followed by `Recovered` through `HandleActivity`;
- on failure: shows an error dialog and does not claim recovery success.

The synthetic activity has no browser connection ID/tab/window identity and uses detail `recovery_self_test`, preventing real browser recovery execution.

## Error handling

- A self-test failure never changes production recovery state.
- A failed self-test does not restart the tray or browser.
- Repeated clicks while one test is running are ignored.
- The result text must identify itself as a test so it cannot be confused with a real ChatGPT outage.

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
- after the first popup is clicked, a test `Recovered` popup appears;
- the activity store ends with the synthetic activity in `Recovered`, not `RecoveryRequired`;
- the test event detail is not `disconnected_waiting`, so no production browser reload is scheduled.

### Full regression

Require the existing Windows PR CI to remain green: browser extension validation, PowerShell installer tests, desktop shortcut tests, core/recovery/RecoveryRunner tests, Windows UI/mobile notification regressions, full solution build, and EventBridge integration.

## Remaining live-validation boundary

This self-test does not prove that a future ChatGPT DOM revision will still expose a real timeout/disconnection in a form the extension detector recognizes. That final detector boundary remains naturally validated when an actual transient ChatGPT error occurs. The test intentionally avoids manufacturing a live DOM failure because safety and false-positive avoidance take priority over artificially forcing that last boundary.
