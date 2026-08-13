# Stop Hook JSON Success Design

## Goal

Codex 0.147.0의 `Stop` Hook이 작업 완료 알림을 전달한 뒤 유효한 성공 JSON을 반환해, `invalid stop hook JSON output` 없이 턴을 정상 종료하게 한다.

## Root cause

Codex Usage Tray EventBridge는 `Stop`일 때 stdout에 `{}`를 출력한다. 공식 Codex Hook 계약은 종료 코드 0인 `Stop` 출력이 이벤트 스키마에 맞는 JSON이어야 한다고 규정하며, 공통 성공 필드는 `continue: true`이다. 실제 Codex 0.147.0은 빈 객체를 유효한 Stop 결과로 인정하지 않고 Hook 실패로 표시한다.

## Design

- Core에 `HookProtocolOutput.GetSuccessJson(string? eventName)` 순수 함수를 둔다.
- `Stop`에는 정확히 `{"continue":true}`를 반환한다.
- `UserPromptSubmit`, `PermissionRequest`, 알 수 없는 이벤트에는 빈 문자열을 반환해 기존 출력 동작을 보존한다.
- EventBridge는 알림 파싱·전달의 성공 여부와 무관하게 `finally`에서 포맷터 결과를 stdout으로 보낸다. 따라서 알림 부가기능 실패가 Codex 턴을 막지 않는다.
- 기존 ASCII 배치 래퍼, 15초 제한, 사용자 Hook 병합, 웹 ChatGPT 경로는 변경하지 않는다.

## Reproduction and tests

1. 현재 `{}` 출력이 공식 성공 JSON 기대값과 달라 실패하는 Core 회귀 테스트를 먼저 추가한다.
2. 포맷터를 구현해 테스트를 통과시킨다.
3. 빈 이벤트명과 비-Stop 이벤트가 출력 오염을 만들지 않는지 적대적으로 검사한다.
4. Windows CI에서 Core, PowerShell 설치, Windows UI, 전체 Release 빌드를 실행한다.
5. 패치 릴리스 후 사용자가 Codex를 재시작하고 `/hooks` 신뢰 상태에서 짧은 턴을 실행해 실제 상단 완료 팝업과 Hook 성공 표시를 확인한다.

## Failure handling and rollback

- EventBridge 내부 오류는 계속 삼키고 Stop 성공 JSON은 반환한다.
- 회귀가 생기면 `v1.2.1` 설치 명령 또는 해당 릴리스 ZIP으로 되돌릴 수 있다.
- `agentmemory` MCP 초기화 오류는 별도 구성 문제이며 이번 변경에 포함하지 않는다.

## Acceptance criteria

- `Stop` 성공 출력이 정확히 `{"continue":true}`이다.
- 비-Stop Hook stdout은 기존처럼 비어 있다.
- 알림 파싱 또는 IPC 실패가 Stop Hook 실패로 전파되지 않는다.
- Windows CI 전 항목이 통과한다.
- `v1.2.2` 릴리스에 ZIP과 SHA-256 자산이 생성된다.
