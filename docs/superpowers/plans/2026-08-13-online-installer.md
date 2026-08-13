# Codex Usage Tray Online Installer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Publish Codex Usage Tray from a public GitHub repository and install or update its prebuilt Windows release with one Windows PowerShell 5.1 command.

**Architecture:** A release-only installer performs staged local replacement without building source. A self-contained online bootstrap downloads the latest GitHub Release ZIP and checksum, verifies SHA-256, expands it to a unique temporary directory, and invokes the release installer. A Windows GitHub Actions workflow tests C# and PowerShell, publishes the two self-contained executables, packages the installer files, and creates the release only after all checks pass.

**Tech Stack:** .NET 8, C# WinForms, Windows PowerShell 5.1, GitHub Actions `windows-latest`, GitHub CLI Release publishing.

## Global Constraints

- Public repository: `alsdmlals4-eng/CodexUsageTray`.
- Default install modifies only the current user: `%LOCALAPPDATA%\CodexUsageTray`, HKCU startup, and the user Codex Hook file.
- Release installation must not require a local .NET SDK.
- Every `.ps1` containing Korean text must remain UTF-8 with BOM for Windows PowerShell 5.1.
- Existing non-app Codex Hook handlers must survive install, update, and removal.
- A missing, malformed, or mismatched checksum must stop before changing the installed application.
- No API key, paid service, administrator privilege, code-signing certificate, or additional runtime dependency.
- No license file is added without a separate user decision.

---

### Task 1: Release Installer with Staged Replacement

**Files:**
- Create: `scripts/install-release.ps1`
- Create: `tests/PowerShellInstaller.Tests.ps1`
- Modify: `scripts/build-windows.ps1`

**Interfaces:**
- Consumes: a package directory containing `CodexUsageTray.exe`, `CodexUsageTray.EventBridge.exe`, `setup-integration.ps1`, `remove-integration.ps1`, and `install-release.ps1`.
- Produces: `scripts/install-release.ps1 -PackageDirectory <path> -InstallDirectory <path> [-StartWithWindows] [-DoNotLaunch] [-SkipCodexHooks]`.

- [ ] **Step 1: Write a failing PowerShell installer test**

Create a dependency-free test runner that makes a temporary fake package, invokes `install-release.ps1` with `-DoNotLaunch -SkipCodexHooks`, verifies all five package files are copied, replaces the fake executable with a second version, invokes the installer again, verifies the new content, and verifies that no `.update-*` or `.backup-*` directory remains. It must also parse every repository `.ps1` with `System.Management.Automation.Language.Parser` and require the UTF-8 BOM bytes `EF BB BF`.

```powershell
$package = Join-Path $testRoot 'package'
$install = Join-Path $testRoot 'installed'
& $installer -PackageDirectory $package -InstallDirectory $install -DoNotLaunch -SkipCodexHooks
Assert-Equal 'version-1' (Get-Content (Join-Path $install 'CodexUsageTray.exe') -Raw)
Set-Content -LiteralPath (Join-Path $package 'CodexUsageTray.exe') -Value 'version-2' -NoNewline
& $installer -PackageDirectory $package -InstallDirectory $install -DoNotLaunch -SkipCodexHooks
Assert-Equal 'version-2' (Get-Content (Join-Path $install 'CodexUsageTray.exe') -Raw)
```

- [ ] **Step 2: Run the test to verify RED**

Run on Windows PowerShell 5.1:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\PowerShellInstaller.Tests.ps1
```

Expected: FAIL because `scripts/install-release.ps1` does not exist.

- [ ] **Step 3: Implement staged installation**

Implement these exact phases:

1. Validate the five required package files before stopping anything.
2. Stop only a `CodexUsageTray` process whose `Path` equals the final installed executable path.
3. Copy package files to `$InstallDirectory.update-$PID`.
4. Move an existing installation to `$InstallDirectory.backup-$PID`.
5. Move the staged directory to `$InstallDirectory`.
6. Run `setup-integration.ps1` unless skipped.
7. Register `"<installed exe>" --startup` in HKCU when requested.
8. Launch unless disabled.
9. Delete the backup only after success.
10. On failure, delete the failed new directory, restore the backup, relaunch a restored app when it was running, and rethrow.

The source build script must run `PowerShellInstaller.Tests.ps1` before C# tests.

- [ ] **Step 4: Run the test to verify GREEN**

Run the command from Step 2. Expected: all installer, BOM, and AST parsing checks PASS.

- [ ] **Step 5: Commit Task 1**

```bash
git add scripts/install-release.ps1 scripts/build-windows.ps1 tests/PowerShellInstaller.Tests.ps1
git commit -m "feat: add staged release installer"
```

### Task 2: Checksummed Online Bootstrap

**Files:**
- Create: `install-online.ps1`
- Modify: `tests/PowerShellInstaller.Tests.ps1`

**Interfaces:**
- Consumes: `https://github.com/alsdmlals4-eng/CodexUsageTray/releases/latest/download/CodexUsageTray-win-x64.zip` and the same URL with `.sha256` appended.
- Produces: `Install-CodexUsageTrayOnline`, plus the public one-line entrypoint `irm <raw URL> | iex`.

- [ ] **Step 1: Add failing checksum tests**

Dot-source `install-online.ps1 -LibraryOnly` and test:

```powershell
$expected = 'A' * 64
Set-Content -LiteralPath $checksum -Value "$expected  CodexUsageTray-win-x64.zip"
Assert-Equal $expected (Read-ExpectedSha256 -Path $checksum)
Assert-Throws { Read-ExpectedSha256 -Path $malformedChecksum }
Assert-Throws { Assert-ArchiveSha256 -ArchivePath $archive -ExpectedSha256 ('0' * 64) }
```

Expected RED: the online script and checksum functions do not exist.

- [ ] **Step 2: Implement the bootstrap**

`install-online.ps1` must:

- define `$Repository = 'alsdmlals4-eng/CodexUsageTray'` and fixed latest-release URLs;
- enable TLS 1.2 without removing newer protocols;
- create `CodexUsageTray-<guid>` under the system temp directory;
- use `Invoke-WebRequest -UseBasicParsing` to download ZIP and checksum;
- accept only a 64-character hexadecimal checksum token;
- compare it with `Get-FileHash -Algorithm SHA256` using ordinal case-insensitive equality;
- expand into a new subdirectory;
- require `install-release.ps1` at the archive root;
- invoke it with `-StartWithWindows`;
- remove the entire unique temp directory in `finally`;
- skip automatic execution only when dot-sourced with `-LibraryOnly`.

- [ ] **Step 3: Run checksum tests to verify GREEN**

Run `PowerShellInstaller.Tests.ps1`. Expected: valid checksum accepted, malformed and mismatched checksums rejected, all earlier tests remain green.

- [ ] **Step 4: Commit Task 2**

```bash
git add install-online.ps1 tests/PowerShellInstaller.Tests.ps1
git commit -m "feat: add checksummed online installer"
```

### Task 3: Windows Release Workflow and Documentation

**Files:**
- Create: `.github/workflows/release.yml`
- Modify: `README.md`

**Interfaces:**
- Consumes: a `v*` tag or manual `version` input.
- Produces: Release assets `CodexUsageTray-win-x64.zip` and `CodexUsageTray-win-x64.zip.sha256`.

- [ ] **Step 1: Add a workflow structure check to the PowerShell test**

Require the workflow text to contain `windows-latest`, `dotnet run`, both publish projects, `PowerShellInstaller.Tests.ps1`, both expected asset names, `Get-FileHash`, and `gh release create`. Expected RED while the workflow is absent.

- [ ] **Step 2: Implement `.github/workflows/release.yml`**

Use only `actions/checkout@v4`, `actions/setup-dotnet@v4`, built-in PowerShell, `dotnet`, and `gh`. Grant `contents: write`; select the tag from `GITHUB_REF_NAME` or the required manual input; run PowerShell and C# tests; publish both projects self-contained to one package directory; copy the three integration/install scripts; run a temporary-install smoke check; compress the five files; write the lowercase SHA-256 followed by two spaces and the ZIP name; and call:

```powershell
gh release create $env:RELEASE_TAG `
  'release/CodexUsageTray-win-x64.zip' `
  'release/CodexUsageTray-win-x64.zip.sha256' `
  --repo $env:GITHUB_REPOSITORY `
  --target $env:GITHUB_SHA `
  --title "Codex Usage Tray $env:RELEASE_TAG" `
  --generate-notes
```

- [ ] **Step 3: Update README**

Lead installation with the one-line command. Keep a safer review-first alternative that downloads `install-online.ps1` to `$env:TEMP`, opens it in Notepad, and runs it. Explain checksum validation, current-user scope, updates with the same command, removal, SmartScreen limitation, and source-build instructions as the developer path.

- [ ] **Step 4: Run static verification**

Run PowerShell installer tests on Windows, C# core tests, YAML parsing, C#/PowerShell syntax parsing, `git diff --check`, placeholder scan, and secret scan. Expected: all available checks green with no credentials or generated binaries committed.

- [ ] **Step 5: Commit Task 3**

```bash
git add .github/workflows/release.yml README.md tests/PowerShellInstaller.Tests.ps1
git commit -m "ci: publish verified Windows releases"
```

### Task 4: Public Repository and First Release

**Files:**
- No source changes expected after Task 3 unless CI reveals a verified defect.

**Interfaces:**
- Consumes: authenticated GitHub CLI and the clean local feature branch.
- Produces: public `alsdmlals4-eng/CodexUsageTray`, default `main`, tag `v1.1.1`, and its Release assets.

- [ ] **Step 1: Verify GitHub authority and local scope**

Run:

```bash
gh --version
gh auth status
git status -sb
git log --oneline --decorate -8
```

Expected: authenticated as an account permitted to create `alsdmlals4-eng` repositories; clean worktree; only approved commits in scope.

- [ ] **Step 2: Create and publish the repository**

```bash
gh repo create alsdmlals4-eng/CodexUsageTray --public --description "Windows tray usage and activity notifications for Codex"
git remote add origin https://github.com/alsdmlals4-eng/CodexUsageTray.git
git push origin HEAD:main
gh repo edit alsdmlals4-eng/CodexUsageTray --default-branch main
```

Do not add a license or unrelated Base files.

- [ ] **Step 3: Tag the first release**

```bash
git tag -a v1.1.1 -m "Codex Usage Tray v1.1.1"
git push origin v1.1.1
gh run list --repo alsdmlals4-eng/CodexUsageTray --limit 5
run_id=$(gh run list --repo alsdmlals4-eng/CodexUsageTray --workflow release.yml --limit 1 --json databaseId --jq '.[0].databaseId')
gh run watch "$run_id" --repo alsdmlals4-eng/CodexUsageTray --exit-status
```

Expected: the release workflow succeeds.

- [ ] **Step 4: Verify public artifacts and installer endpoints**

Check `gh release view v1.1.1 --repo alsdmlals4-eng/CodexUsageTray --json assets,url`, fetch both public asset URLs without authentication, compare the downloaded SHA-256, and confirm the raw `install-online.ps1` URL returns the committed BOM-aware script.

- [ ] **Step 5: Report the Windows acceptance command**

Provide exactly:

```powershell
irm https://raw.githubusercontent.com/alsdmlals4-eng/CodexUsageTray/main/install-online.ps1 | iex
```

State that GitHub CI and public-download verification passed, while final tray/Hook/SmartScreen behavior still requires one run on the user's Windows machine.
