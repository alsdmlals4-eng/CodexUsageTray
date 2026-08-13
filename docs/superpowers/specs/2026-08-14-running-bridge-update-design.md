# Running EventBridge Update Design

## Goal

웹 ChatGPT 확장의 네이티브 호스트가 실행 중인 상태에서도 기존 설치를 손상하지 않고 Codex Usage Tray를 업데이트한다.

## Root cause

`install-release.ps1`은 설치 경로의 `CodexUsageTray.exe`만 찾아 종료한다. 웹 확장은 `CodexUsageTray.EventBridge.exe`와 지속적인 네이티브 메시징 연결을 유지하고 연결이 끊기면 1초 후 재연결한다. 설치기는 트레이를 종료한 뒤 패키지를 스테이징하므로, 실행 중인 EventBridge 또는 스테이징 중 재시작된 EventBridge가 설치 폴더를 잠가 기존 폴더의 백업 이동을 실패시킨다.

## Design

- 패키지 유효성 검사와 스테이징 복사를 프로세스 종료보다 먼저 완료한다.
- 설치 경로와 정확히 일치하는 `CodexUsageTray.exe` 및 `CodexUsageTray.EventBridge.exe`만 종료한다.
- 다른 폴더에서 실행 중인 동명 프로세스는 종료하지 않는다.
- 프로세스 종료 후 기존 설치 폴더를 즉시 백업 위치로 이동한다.
- 브라우저 확장의 1초 재연결 경쟁으로 이동이 잠기면 정확한 설치 경로의 대상 프로세스를 다시 종료하고 짧게 제한 재시도한다.
- 교체 이후 Hook과 브라우저 네이티브 호스트 등록은 기존 `setup-integration.ps1` 경로를 그대로 사용한다.
- 실패하면 새 설치를 제거하고 백업을 원래 경로로 복원한다. 업데이트 전 트레이가 실행 중이었을 때만 기존 트레이를 다시 실행한다.
- EventBridge는 브라우저 확장이 필요할 때 자동으로 다시 연결하므로 실패 복구 시 직접 실행하지 않는다.

## Test strategy

1. Windows 테스트에서 실제 실행 가능한 잠금 보유 프로그램을 `CodexUsageTray.EventBridge.exe`라는 이름으로 설치 폴더에 생성한다.
2. 그 프로세스를 실행한 채 릴리스 설치기로 업데이트한다.
3. 기존 v1.2.2 설치기는 폴더 이동 잠금으로 실패하는 RED를 확인한다.
4. 수정 후 설치가 성공하고, EventBridge 잠금 프로세스가 종료되며, 새 파일이 활성화되고, 스테이지·백업 폴더가 남지 않는지 확인한다.
5. PowerShell 5.1 구문 검사, 기존 설치·업데이트, Hook 통합, Core, Windows UI, 빌드, 게시 패키지 smoke test를 모두 재실행한다.

## Adversarial boundaries

- 설치 경로 밖의 동명 프로세스는 보호한다.
- 트레이만 실행 중인 기존 업데이트 동작을 보존한다.
- EventBridge만 실행 중이어도 실패 복구가 트레이를 잘못 시작하지 않는다.
- 대상 프로세스가 종료되지 않으면 기존 설치를 이동하기 전에 명확히 실패한다.
- 스테이징 또는 Hook 설정 실패 시 기존 설치가 복구된다.
- `-DoNotLaunch`와 `-SkipCodexHooks`의 기존 의미를 바꾸지 않는다.

## Scope exclusions

- 브라우저 확장을 자동으로 비활성화하거나 Chrome/Edge를 종료하지 않는다.
- 설치 폴더의 모든 프로세스를 광범위하게 종료하지 않는다.
- 버전별 side-by-side 설치 구조로 변경하지 않는다.
- 웹 ChatGPT 완료 감지와 Codex Hook 이벤트 형식은 변경하지 않는다.

## Acceptance criteria

- 웹 확장 EventBridge가 설치 폴더에서 실행 중인 업데이트가 성공한다.
- 설치 경로의 트레이와 EventBridge만 종료된다.
- 실패 시 기존 설치와 트레이 실행 상태가 복구된다.
- Windows 전체 CI와 릴리스 패키지 통합 테스트가 통과한다.
- `v1.2.3` ZIP과 SHA-256 자산이 게시된다.
