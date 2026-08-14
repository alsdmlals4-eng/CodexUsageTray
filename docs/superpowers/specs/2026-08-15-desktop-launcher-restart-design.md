# Desktop Launcher and Restart Design

## Goal

Make Codex Usage Tray directly launchable without PowerShell or manually browsing the install directory, and make restarting the tray app a one-click tray action.

## User-visible behavior

1. A normal installation to `%LOCALAPPDATA%\CodexUsageTray` creates or refreshes a desktop shortcut named `Codex Usage Tray.lnk` that targets the installed `CodexUsageTray.exe`.
2. The tray context menu contains `앱 다시 시작`.
3. Choosing `앱 다시 시작` shuts down the current tray runtime cleanly, starts the installed executable again, and then exits the old instance.
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

Small WinForms-side helper that resolves the current executable and starts exactly that executable. Process launch failures are converted to a false result so the tray can show an actionable error instead of crashing.

### `TrayApplicationContext`

Adds the `앱 다시 시작` menu action. Restart reuses the existing shutdown sequence: stop timers, cancel active work, dispose pipe/client backends, launch the replacement process, then exit the old message loop.

## Error handling

- Shortcut creation failure does not invalidate an otherwise successful install; the installer prints a warning and the executable remains installed/runnable.
- Restart launch failure shows a Windows error dialog and exits the old instance only after the normal backend shutdown path has completed.
- Repeated restart clicks are ignored once shutdown/restart has begun via the existing `_exiting` guard.

## Test contract

1. Windows PowerShell test creates a shortcut in a temporary directory and reopens it with `WScript.Shell` to prove target, working directory, and icon point to the requested executable.
2. Windows UI regression test proves restart launcher targets the exact executable path and converts a process-start exception into a safe failure result.
3. CI solution build proves tray menu wiring compiles with existing WinForms code.
4. Release packaging includes `shortcut-registration.ps1`; smoke install proves it is copied into the installed directory.

## Release/installation

This feature requires a patch release after merge so the existing one-line online installer can deliver the new installer helper and tray executable. After the user runs the same update command once, the desktop shortcut appears and future launches/restarts need no PowerShell window.
