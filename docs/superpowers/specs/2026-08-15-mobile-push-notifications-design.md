# Mobile Push Notifications Design

## Goal

Codex Usage Tray가 이미 감지하는 **Codex 터미널 및 ChatGPT 웹의 승인 필요/작업 완료 이벤트**를 기존 Windows 팝업과 함께 휴대폰 ntfy Push로도 전달한다. PC 앞에 없거나 ChatGPT/Codex 창이 백그라운드에 있어도 사용자가 핵심 상태 변화를 확인할 수 있어야 한다.

## Current architecture

현재 `main`은 복구 PR #17이 병합된 구조다.

- Codex CLI: Hook → `CodexUsageTray.EventBridge` → activity pipe → `TrayApplicationContext.HandleActivity()`
- ChatGPT Web: 브라우저 확장 → native messaging EventBridge → activity pipe → `HandleActivity()`
- 복구: `HandleActivity()` → `BrowserRecoveryCoordinator` → 필요 시 정확한 원본 탭 reload

모바일 전송은 두 수집 경로가 합쳐진 `HandleActivity()` 뒤에서 별도 fan-out으로 동작하며, 기존 복구 계획보다 앞에서 호출되더라도 모든 동기/비동기 실패를 자체 격리한다.

## Selected approach

### Tray 공통 처리 지점에서 fan-out — 채택

장점:

- Codex와 ChatGPT Web을 한 구현으로 처리한다.
- Hook/브라우저 감지 프로토콜을 바꾸지 않는다.
- ntfy 장애가 Hook, native messaging, Windows popup, recovery coordinator를 방해하지 않는다.
- 향후 모바일 공급자를 바꿔도 이벤트 수집부는 유지된다.

### EventBridge 직접 전송 — 제외

Hook stdout/timeout 및 native messaging 경계에 네트워크 책임을 추가하므로 제외한다.

### 브라우저 확장 직접 전송 — 제외

ChatGPT Web에만 적용되고 topic 노출/host permission이 추가되므로 제외한다.

## Components

### `MobileNotificationSettingsStore`

- 경로: `%LOCALAPPDATA%\CodexUsageTrayData\mobile-notifications.json`
- 값: `Enabled`, `Topic`
- 설치 교체 경로 `%LOCALAPPDATA%\CodexUsageTray` 밖에 저장한다.
- 파일 누락/손상/읽기 오류는 모바일 비활성 상태로 fail closed 한다.

### `NtfyPushClient`

- `https://ntfy.sh` 루트에 JSON POST한다.
- topic은 URL에 넣지 않고 JSON body의 `topic` 필드로 보낸다.
- 기본 timeout은 5초다.
- 주입한 `HttpClient`로 네트워크 계약을 테스트할 수 있다.

### `MobilePushNotifier`

- 대상: `ApprovalRequired`, `Completed`
- 비대상: `Running`, `Retrying`, `RecoveryRequired`, `Recovered`
- 같은 `ActivityKey + Status`는 실행 중 한 번만 보낸다.
- 최대 256개 dedupe key만 메모리에 유지한다.
- `ApprovalRequired -> Completed` 상태 전이는 각각 한 번 허용한다.
- sender/settings/diagnostic 실패를 원 작업으로 전파하지 않는다.

### `MobileNotificationSettingsForm`

- 휴대폰 알림 활성화
- ntfy topic 입력
- 저장
- 테스트 알림 전송
- topic은 `UseSystemPasswordChar`로 가려 표시
- topic 형식은 ASCII 영문/숫자/`-`/`_`, 1~64자로 제한

### `MobileNotificationRuntime`

- settings store, ntfy client, notifier를 묶는다.
- shared 진입점 자체도 예외를 잡아 `TrayApplicationContext`를 보호한다.
- 설정창 생성과 실제 activity Push를 같은 설정/전송 계층으로 통일한다.

## Notification presentation

승인:

- `승인 필요 · Codex`
- `승인 필요 · ChatGPT`

완료:

- `작업 완료 · Codex`
- `작업 완료 · ChatGPT`

본문은 기존 안전한 메타데이터만 사용한다.

- ChatGPT Web: 채팅 라벨 + Summary
- Codex: 프로젝트 + 채팅 라벨 + Summary

`Detail`, 대화 본문, Hook 원문, 쿠키, 토큰, 환경 변수는 보내지 않는다.

## Failure isolation

다음 실패는 Windows 알림과 복구 흐름을 방해하면 안 된다.

- 인터넷 없음
- DNS/HTTPS 실패
- ntfy HTTP 오류
- 설정 파일 손상
- 잘못된 topic
- 앱 종료 중 cancellation
- diagnostic write 실패

모바일 diagnostic에는 exception message를 쓰지 않는다. 실패 종류와 선택적 HTTP status만 기록하여 exception text에 topic이 섞여 있어도 영구 로그에 남기지 않는다.

## Recovery compatibility

복구 PR #17의 상태와 동작은 정본으로 유지한다.

- `Retrying`: 모바일 미전송
- `RecoveryRequired`: 모바일 미전송
- `Recovered`: 모바일 미전송
- 브라우저 reconnect backoff/원본 탭 제한은 변경하지 않는다.
- 모바일 전송 실패가 `BrowserRecoveryCoordinator.Plan()` 호출을 막지 않는다.

향후 복구 상태도 휴대폰에 보내고 싶다면 별도 정책 변경으로 검토한다. 이번 기능에 섞지 않는다.

## Test strategy

1. 설정 저장/로드와 손상 JSON fail-closed.
2. 완료/승인만 Push되고 복구 상태는 Push되지 않음.
3. 동일 activity/status 중복 억제와 승인→완료 전이 허용.
4. sender 예외가 notifier 밖으로 전파되지 않음.
5. ntfy request URL에 topic이 포함되지 않고 JSON body에 들어감.
6. diagnostic에 exception 속 topic이 남지 않음.
7. 설정 UI가 topic을 마스킹하고 저장/테스트 전송을 수행함.
8. 잘못된 topic을 활성 설정으로 저장하지 않음.
9. runtime이 저장된 topic으로 실제 notifier 경로를 호출함.
10. 기존 Windows regression을 먼저 실행한 뒤 모바일 regression을 실행함.
11. 현행 recovery/Core/RecoveryRunner/installer/browser/Hook CI를 그대로 통과함.

## Scope exclusions

- 모바일 원격 승인/거절
- PC 원격 제어
- Firebase 직접 연동
- 자체 모바일 앱
- 자체 ntfy 서버
- 영구 재시도 큐
- 복구 상태 모바일 Push

## Acceptance criteria

- ChatGPT Web 완료 시 PC/tray가 실행 중이면 휴대폰 Push 경로가 실행된다.
- Codex 완료/승인 필요도 같은 경로를 사용한다.
- ChatGPT/Codex 창이 전면일 필요가 없다.
- 기존 Windows popup/history와 복구 기능이 회귀하지 않는다.
- 동일 턴/상태 Push가 중복 증폭되지 않는다.
- ntfy 장애가 Hook, browser bridge, recovery, Windows popup을 막지 않는다.
- 사용자 topic은 저장소/릴리스/diagnostics에 들어가지 않는다.
- 앱 업데이트로 사용자 모바일 설정이 삭제되지 않는다.
