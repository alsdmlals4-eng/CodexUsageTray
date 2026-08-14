# Desktop Launcher and Restart Design

## Goal

Make Codex Usage Tray directly launchable without PowerShell or manually browsing the install directory, and make restarting the tray app a one-click tray action.

## User-visible behavior

1. A normal installation to `%LOCALAPPDATA%\CodexUsageTray` creates or refreshes a desktop shortcut named `Codex Usage Tray.lnk` that targets the installed `CodexUsageTray.exe`.
2. The tray context menu contains `앱 다시 시작`.
3. Choosing `앱 다시 시작` shuts down the current tray runtime cleanly and hands off to a replacement instance without requiring PowerShell or another launcher.
4. Existing `Windows 시작 시 실행`, mobile ntfy settings, ChatGPT extension integration, recovery behavior, and Codex hooks continue unchanged.

## Boundaries

- No new background service, launcher executable, browser permission, API key, or network dependency.
- Do not change browser-extension code, EventBridge protocol, RecoveryRunner, ActivityStatus, ntfy topic persistence, or Codex hook semantics.
- Custom/test install directories do not create or overwrite the user's real desktop shortcut automatically; automatic desktop shortcut creation is restricted to the canonical `%LOCALAPPDATA%\CodexUsageTray` install path.
- The shortcut is user-scoped and uses the current user's Desktop directory.

## Components

### `scripts/shortcut-registration.ps1`

Owns Windows shortcut creation. It creates/updates a `.lnk` with:

- target: installed `CodexUsageTray.exe`
- working directory: the install directory
- icon: the installed executable
- description: `Codex Usage Tray 실행`

The helper supports a library-only mode so Windows CI can exercise it without running the full installer.

### `scripts/install-release.ps1`

Copies the shortcut helper into the installed package. After the installation transaction is committed, it creates/refreshes the desktop shortcut only for the canonical install directory. Shortcut creation is best-effort after a successful install: a shortcut failure must not roll back a valid application update.

### `ApplicationRestartLauncher`

Starts the exact current executable with `--restart-after <current PID>`. It does not use PowerShell or a separate launcher process. Process launch failures are converted to a false result so the tray can show an actionable error instead of crashing.

### `ApplicationRestartStartup`

Runs before the single-instance mutex is acquired. Normal startup arguments such as `--startup` continue immediately. A restart replacement validates the `--restart-after` PID and waits up to 15 seconds for that exact previous process to exit. Once the old process releases `Local\CodexUsageTray`, the replacement proceeds to the existing mutex check and normal tray startup. Malformed or unsafe restart arguments fail closed.

### `TrayApplicationContext`

Adds the `앱 다시 시작` menu action. Restart reuses the existing shutdown sequence: stop timers, cancel active work, dispose pipe/client backends, launch the replacement with the current PID, then exit the old message loop. The replacement waits for the old process before trying to acquire the single-instance mutex.

## Error handling

- Shortcut creation failure does not invalidate an otherwise successful install; the installer prints a warning and the executable remains installed/runnable.
- Restart launch failure shows a Windows error dialog instead of throwing.
- The replacement waits at most 15 seconds for the previous PID; malformed PIDs or wait failures terminate the replacement rather than bypassing the single-instance contract.
- Repeated restart clicks are ignored once shutdown/restart has begun via the existing `_exiting` guard.

## Test contract

1. Windows PowerShell test creates a shortcut in a temporary directory and reopens it with `WScript.Shell` to prove target, working directory, and icon point to the requested executable.
2. Windows UI regression tests prove the restart launcher targets the exact executable path, passes the previous PID, and converts process-start exceptions into safe failure results.
3. Restart-startup tests prove normal startup is unaffected, a valid restart waits for the exact previous PID, and malformed PIDs fail closed.
4. Tray regression tests prove `앱 다시 시작` exists and passes the current executable path and process ID to the launcher.
5. CI solution build proves the pre-mutex restart handoff and tray menu wiring compile with existing WinForms code.
6. Release packaging includes `shortcut-registration.ps1`; smoke install proves it is copied into the installed directory.

## Adversarial review finding

An initial implementation started the replacement executable while the old process still held the existing `Local\CodexUsageTray` mutex. The replacement therefore exited immediately as a second instance, after which the old process also exited. The design was corrected before merge to use the bounded `--restart-after <PID>` handoff described above.

## Release/installation

This feature requires a patch release after merge so the existing one-line online installer can deliver the new installer helper and tray executable. After the user runs the same update command once, the desktop shortcut appears and future launches/restarts need no PowerShell window.
