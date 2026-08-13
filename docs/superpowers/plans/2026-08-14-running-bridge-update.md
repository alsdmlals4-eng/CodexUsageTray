# Running EventBridge Update Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 실행 중인 웹 ChatGPT EventBridge가 설치 폴더를 잠근 상태에서도 안전하게 Codex Usage Tray를 업데이트한다.

**Architecture:** 설치 패키지를 먼저 스테이징하고, 설치 경로가 정확히 일치하는 트레이와 EventBridge 프로세스만 종료한 뒤 기존 폴더를 백업 위치로 교체한다. 브라우저 재연결 경쟁은 정확한 대상 프로세스 재탐색과 제한 재시도로 흡수하고, 기존 롤백 경계를 보존한다.

**Tech Stack:** Windows PowerShell 5.1, .NET Framework `Add-Type` test fixture, GitHub Actions Windows runner

## Global Constraints

- 다른 설치 경로의 동명 프로세스를 종료하지 않는다.
- 기존 설치를 백업하기 전에 새 패키지 스테이징을 완료한다.
- 업데이트 전 트레이가 실행 중이었을 때만 실패 복구 시 트레이를 다시 실행한다.
- EventBridge, 웹 ChatGPT 감지, Codex Hook 프로토콜 코드는 변경하지 않는다.
- `v1.2.3` 릴리스 전 Windows 전체 CI와 게시 패키지 테스트가 모두 성공해야 한다.

---

### Task 1: Reproduce the EventBridge directory lock

**Files:**
- Modify: `tests/PowerShellInstaller.Tests.ps1`

**Interfaces:**
- Consumes: `scripts/install-release.ps1 -PackageDirectory -InstallDirectory -DoNotLaunch -SkipCodexHooks`
- Produces: 실행 중인 설치 경로 EventBridge를 종료하고 업데이트해야 하는 회귀 계약

- [ ] **Step 1: Add the failing Windows integration case**

`Add-Type -OutputType ConsoleApplication`으로 30초 대기하는 실행 파일을 설치 경로의 `CodexUsageTray.EventBridge.exe`에 만들고 실행한다. 새 패키지의 EventBridge fixture를 별도 경로에 유지한 채 설치기를 호출하고 다음 리터럴 결과를 단언한다.

```powershell
Assert-True $lockingBridge.HasExited 'update stops the installed EventBridge process'
Assert-Equal 'version-3' (
    [System.IO.File]::ReadAllText((Join-Path $installDirectory 'CodexUsageTray.exe'))
) 'update succeeds while the installed EventBridge is running'
```

- [ ] **Step 2: Run Windows CI and verify RED**

Run the PR workflow on GitHub Actions.

Expected: `Move-Item` in `install-release.ps1` fails because the running EventBridge still locks the install directory.

- [ ] **Step 3: Commit the reproduction**

```bash
git add tests/PowerShellInstaller.Tests.ps1
git commit -m "test: reproduce running EventBridge update lock"
```

### Task 2: Stop exact installed processes after staging

**Files:**
- Modify: `scripts/install-release.ps1`
- Modify: `tests/PowerShellInstaller.Tests.ps1`

**Interfaces:**
- Consumes: exact installed executable paths and the staged update directory
- Produces: `Get-InstalledProcesses`, `Stop-InstalledProcesses`, and bounded install-directory move behavior

- [ ] **Step 1: Move staging before process shutdown**

Create and populate `$stageDirectory` before discovering or stopping installed processes. Do not move or delete `$resolvedInstall` until staging succeeds.

- [ ] **Step 2: Implement exact-path process discovery**

Discover only `CodexUsageTray` and `CodexUsageTray.EventBridge` processes whose normalized `.Path` equals `$installedExecutable` or `$installedBridge`. Record `$trayWasRunning` independently from all stopped processes.

- [ ] **Step 3: Implement bounded shutdown and move retry**

Stop discovered target processes, wait up to five seconds for exit, then move the install directory. On `IOException` or `UnauthorizedAccessException`, rediscover and stop exact-path targets and retry for at most five seconds with 100 ms spacing; rethrow the final exception if the deadline expires.

- [ ] **Step 4: Preserve rollback launch semantics**

In the catch path, restore the backup as before and restart `$installedExecutable` only when `$trayWasRunning` is true. Never directly restart EventBridge.

- [ ] **Step 5: Run Windows CI and verify GREEN**

Expected: PowerShell syntax and installer assertions, including the real locking EventBridge case, all pass; Core, Windows UI, browser, build, and Hook integration remain green.

- [ ] **Step 6: Commit the minimal fix**

```bash
git add scripts/install-release.ps1 tests/PowerShellInstaller.Tests.ps1
git commit -m "fix: update while EventBridge is running"
```

### Task 3: Adversarial review and release v1.2.3

**Files:**
- Modify: `README.md`
- Modify in release PR: `.release-version`

**Interfaces:**
- Consumes: verified running-process update behavior
- Produces: user recovery guidance and `v1.2.3` release assets

- [ ] **Step 1: Attack realistic mutations**

Review whether tests fail if EventBridge discovery is removed, path equality becomes name-only, staging moves after shutdown, rollback starts a tray that was not previously running, or retry becomes unbounded. Add only behavior-level assertions needed to cover an uncovered harmful mutation.

- [ ] **Step 2: Document diagnosis and retry command**

README troubleshooting must explain that v1.2.2 and earlier can fail while the web EventBridge holds the folder, and that installing v1.2.3 or later fixes the running-update path.

- [ ] **Step 3: Verify and merge the implementation PR**

Record RED and GREEN workflow IDs, changed scope, rollback behavior, and remaining user-machine verification in the PR body. Merge only the exact green head SHA.

- [ ] **Step 4: Publish and inspect v1.2.3**

Update `.release-version` to `v1.2.3` in a release PR, pass Windows CI, merge, and verify the release workflow, ZIP, and `.sha256` assets through GitHub.

- [ ] **Step 5: Hand off user-machine reproduction**

Run the one-line installer while the tray and ChatGPT web extension are active. Success requires download verification, installation completion, automatic tray relaunch, and working web/Codex alerts without manually closing Chrome or the extension.
