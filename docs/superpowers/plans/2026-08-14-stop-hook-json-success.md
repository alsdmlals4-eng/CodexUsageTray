# Stop Hook JSON Success Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Codex 0.147.0 Stop Hook이 완료 알림 후 명시적 성공 JSON을 반환하도록 수정한다.

**Architecture:** Hook 프로토콜 출력 결정을 Core 순수 함수로 분리하고 EventBridge의 `finally` 경계에서 사용한다. 알림 전달 로직과 성공 응답 로직을 분리해 IPC 실패도 Codex 턴 실패로 전파되지 않게 한다.

**Tech Stack:** .NET 8, C#, Windows PowerShell 5.1, GitHub Actions

## Global Constraints

- `Stop` stdout은 정확히 `{"continue":true}`여야 한다.
- 비-Stop Hook stdout은 빈 문자열이어야 한다.
- 기존 웹 ChatGPT 알림과 사용자 Hook은 변경하지 않는다.
- 알림 실패는 Codex 작업 결과를 변경하지 않는다.

---

### Task 1: Reproduce and fix Stop success output

**Files:**
- Create: `src/CodexUsageTray.Core/HookProtocolOutput.cs`
- Modify: `src/CodexUsageTray.EventBridge/Program.cs`
- Modify: `tests/CodexUsageTray.Core.Tests/Program.cs`

**Interfaces:**
- Produces: `HookProtocolOutput.GetSuccessJson(string? eventName) -> string`
- Consumes: EventBridge에서 stdin의 `hook_event_name`

- [ ] **Step 1: Write the failing regression test**

테스트는 `Stop`의 기댓값을 리터럴 `{"continue":true}`로 단언하고, `UserPromptSubmit`, `PermissionRequest`, `null`은 빈 문자열인지 검사한다.

- [ ] **Step 2: Run the Core test in Windows CI and verify RED**

Expected: `HookProtocolOutput`이 없어 컴파일 실패한다.

- [ ] **Step 3: Implement the minimal formatter and EventBridge integration**

`GetSuccessJson`은 `StringComparison.Ordinal`로 `Stop`만 구분한다. EventBridge `finally`는 반환된 문자열을 그대로 쓴다.

- [ ] **Step 4: Run the full Windows CI and verify GREEN**

Expected: 브라우저, PowerShell, Core, Windows UI, 솔루션 빌드가 모두 성공한다.

### Task 2: Adversarial review and release

**Files:**
- Modify: `README.md`
- Modify: `.release-version`

**Interfaces:**
- Consumes: Task 1의 검증된 Stop 출력 계약
- Produces: `v1.2.2` Windows 릴리스

- [ ] **Step 1: Attack the boundary cases**

빈 입력, 잘못된 이벤트명, 비-Stop 이벤트, 알림 파서 실패, IPC 실패가 잘못된 stdout이나 비정상 종료를 만들 수 있는지 코드와 테스트를 재검토한다.

- [ ] **Step 2: Document diagnosis and user verification**

README에 `invalid stop hook JSON output`의 업데이트·재신뢰·실제 확인 절차를 추가한다.

- [ ] **Step 3: Merge the verified PR**

PR 본문에 원인, RED/GREEN 증거, 적대적 검토 결과, 미검증 항목을 기록한다.

- [ ] **Step 4: Publish and inspect v1.2.2**

릴리스 워크플로 성공, ZIP 및 `.sha256` 자산 업로드를 GitHub API에서 확인한다.

- [ ] **Step 5: Hand off the real Windows reproduction**

사용자는 한 줄 설치 후 확장 새로고침 없이 Codex만 재시작하고 `/hooks`에서 변경된 세 Hook을 신뢰한 다음 짧은 작업을 실행한다. 성공 기준은 `Stop hook (completed)`와 상단 완료 팝업이다.
