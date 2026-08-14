# Mobile Push Notifications Implementation Plan

**Goal:** 기존 Codex terminal/ChatGPT Web의 `ApprovalRequired`와 `Completed` activity를 Windows 팝업과 독립적인 ntfy 휴대폰 Push로 전달한다.

**Base:** recovery PR #17이 병합된 `main` (`3a18fa3b5af8d056473e7f904a53c2cd0fa1783f`).

## Constraints

- Hook stdout/Stop JSON/native messaging 프로토콜은 변경하지 않는다.
- 브라우저 확장 permission은 변경하지 않는다.
- `Running`, `Retrying`, `RecoveryRequired`, `Recovered`는 모바일 Push하지 않는다.
- Windows popup/history와 recovery coordinator는 모바일 실패와 독립적이어야 한다.
- topic은 설치 폴더·저장소·diagnostics에 노출하지 않는다.
- 설정은 `%LOCALAPPDATA%\CodexUsageTrayData\mobile-notifications.json`에 둔다.
- 영구 retry queue, 원격 승인, Firebase, 자체 모바일 앱은 제외한다.

## Task 1 — Settings persistence

- [x] `MobileNotificationSettings` 추가.
- [x] `MobileNotificationSettingsStore` 추가.
- [x] 설치 폴더 밖 기본 경로 사용.
- [x] 손상 JSON/IO 오류를 비활성 설정으로 fail closed.
- [x] 저장 시 topic trim.

## Task 2 — ntfy transport and notifier

- [x] `NtfyPushClient` 추가.
- [x] ntfy root JSON POST 사용.
- [x] topic을 URL이 아니라 JSON body로 전달.
- [x] 5초 기본 timeout.
- [x] `MobilePushNotifier` 추가.
- [x] 완료/승인만 전송.
- [x] 복구 상태 모바일 미전송 계약 추가.
- [x] `ActivityKey + Status` bounded dedupe.
- [x] sender/settings/diagnostic 실패 격리.
- [x] mobile diagnostic에서 exception message 제외.

## Task 3 — Settings UI and tray integration

- [x] `MobileNotificationSettingsForm` 추가.
- [x] topic 마스킹.
- [x] ntfy topic 문자/길이 검증.
- [x] 저장/테스트 알림 기능.
- [x] `MobileNotificationRuntime` 추가.
- [x] shared entry point 자체 예외 격리.
- [x] 트레이 메뉴 `휴대폰 알림 설정` 추가.
- [x] `HandleActivity()`에 모바일 fan-out 추가.
- [x] 기존 recovery plan 호출 순서/로직 보존.

## Task 4 — Regression tests and docs

- [x] 기존 Windows regression entry point 보존.
- [x] 별도 mobile regression runner 추가.
- [x] 설정/정책/dedupe/failure/URL/privacy 테스트 추가.
- [x] UI 저장/검증/test-send/runtime 테스트 추가.
- [x] `docs/MOBILE_NOTIFICATIONS.md` 추가.
- [x] 최신 recovery-aware 설계 문서 추가.
- [ ] 최신 head의 Windows CI 전체 성공 확인.
- [ ] 최종 diff 적대적 검토.
- [ ] 구 draft PR #18 종료.
- [ ] PR #19을 ready 상태로 전환.

## Required verification

CI에서 다음 현행 단계가 모두 성공해야 한다.

- ChatGPT browser extension tests
- PowerShell installer compatibility
- recovery release wiring
- Core tests
- recovery logic tests
- RecoveryRunner tests
- existing Windows UI regressions
- mobile notification regressions
- full solution build
- EventBridge Hook integration

## Final adversarial review

- recovery 상태가 실수로 mobile Push되지 않는가
- topic이 URL/log/source docs에 실제 값으로 노출되지 않는가
- `TrayApplicationContext` 변경이 메뉴 1개 + fan-out 1개 수준인지
- browser extension/Hook/RecoveryRunner 파일을 모바일 기능 때문에 변경하지 않았는가
- ntfy 장애가 browser recovery 계획을 막을 예외 경로가 남아 있지 않은가
- 업데이트가 외부 설정 경로를 삭제하지 않는가
