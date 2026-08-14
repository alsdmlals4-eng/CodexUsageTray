# Mobile Push Notifications Design

## Goal

Codex Usage Tray가 이미 감지하는 **Codex 터미널 및 ChatGPT 웹의 승인 필요/작업 완료 이벤트**를 기존 Windows 팝업과 함께 휴대폰 ntfy 푸시로도 전달한다. PC 앞에 없거나 ChatGPT/Codex 창이 백그라운드에 있어도 사용자가 작업 상태 변화를 즉시 알 수 있어야 한다.

## Current architecture

현재 이벤트 경로는 두 종류다.

- Codex CLI: 공식 Hook(`UserPromptSubmit`, `PermissionRequest`, `Stop`) → `CodexUsageTray.EventBridge` → activity pipe → `TrayApplicationContext.HandleActivity()`
- ChatGPT Web: 브라우저 확장 완료/승인 감지 → native messaging `CodexUsageTray.EventBridge` → activity pipe → `TrayApplicationContext.HandleActivity()`

두 경로가 이미 `HandleActivity()`에서 합쳐지므로 모바일 전송도 이 공통 지점 뒤에 둔다. EventBridge 또는 브라우저 확장에서 직접 ntfy를 호출하지 않는다.

## Considered approaches

### A. Tray 공통 처리 지점에서 fan-out — 채택

`HandleActivity()`가 Windows 기록/팝업 처리와 모바일 알림 전달을 각각 독립적으로 호출한다.

장점:

- Codex와 ChatGPT Web을 한 구현으로 처리한다.
- Hook/브라우저 감지 코드를 변경하지 않아 회귀 위험이 낮다.
- ntfy 장애가 EventBridge, Hook 출력, 브라우저 native messaging을 방해하지 않는다.
- 향후 다른 모바일 공급자로 교체해도 이벤트 수집부는 유지된다.

### B. EventBridge가 직접 ntfy 전송 — 제외

모든 이벤트를 볼 수는 있지만 EventBridge는 Hook 프로토콜과 native messaging 경계를 담당하고 있다. 네트워크 호출을 추가하면 짧은 프로세스 수명, Hook timeout, stdout 프로토콜 경계에 불필요한 위험을 만든다.

### C. 브라우저 확장이 ntfy 전송 — 제외

ChatGPT Web만 처리하게 되어 Codex와 중복 구현이 필요하고, ntfy 토픽을 확장 저장소에 노출하며 추가 host permission/CORS 경계를 만든다.

## Design

### 1. Mobile notification service

새 Windows 앱 구성 요소 `MobilePushNotifier`를 둔다.

- 입력: `ActivityEvent`
- 전송 대상: `https://ntfy.sh/<topic>`
- 전송 이벤트: `ApprovalRequired`, `Completed`
- `Running`은 푸시하지 않는다.
- Windows 팝업 표시 여부와 모바일 전송 성공 여부는 서로 독립적이다.
- 네트워크 호출은 UI thread를 막지 않는 비동기 작업으로 실행한다.
- 짧은 timeout을 사용하고 실패 시 예외를 호출자에게 전파하지 않는다.
- 실패 원인은 진단 로그에 남기되 topic/전체 URL은 절대 기록하지 않는다.

첫 버전에서는 재시도 큐를 만들지 않는다. 오프라인 상태에서 과거 알림을 뒤늦게 대량 전송하는 것보다 현재 Windows 기록을 정본으로 유지한다.

### 2. Event selection and deduplication

모바일 푸시는 현재 Windows 대형 팝업과 동일한 상태인 `ApprovalRequired`와 `Completed`만 대상으로 한다.

같은 `ActivityKey + Status`가 반복 수신될 경우 한 번만 모바일로 보낸다. `Running → ApprovalRequired → Completed`처럼 상태가 실제로 바뀌면 각 대상 상태는 각각 한 번 전송할 수 있다. 중복 억제 상태는 메모리에 제한된 개수만 유지하고 앱 재시작 후에는 초기화한다.

### 3. Notification presentation

제목:

- 승인 필요: `승인 필요 · Codex` 또는 `승인 필요 · ChatGPT`
- 완료: `작업 완료 · Codex` 또는 `작업 완료 · ChatGPT`

본문은 기존 `ActivityEvent`의 안전한 메타데이터만 사용한다.

- ChatGPT Web: 채팅 제목/라벨 + `Summary`
- Codex: 프로젝트명 + 채팅 라벨 + `Summary`

대화 본문, Hook 원문, 쿠키, 토큰, 환경 변수는 전송하지 않는다.

첫 버전에서는 모바일 알림 탭으로 PC 터미널 제어, 원격 승인, 원격 명령 실행을 제공하지 않는다.

### 4. Local settings

ntfy 토픽은 저장소나 설치 패키지에 포함하지 않는다.

설치기는 `%LOCALAPPDATA%\CodexUsageTray` 설치 폴더 전체를 교체하므로 사용자 설정은 별도 위치에 둔다.

- 설정 경로: `%LOCALAPPDATA%\CodexUsageTrayData\mobile-notifications.json`
- 저장 값: `Enabled`, `Topic`
- 서버는 첫 버전에서 `https://ntfy.sh`로 고정한다.
- topic은 화면/로그에 전체 노출하지 않고 필요하면 일부만 마스킹한다.

Windows 사용자 프로필의 로컬 설정 파일 수준의 보호를 사용한다. 별도 계정 인증, 자체 ntfy 서버, 토큰 인증은 이번 범위에 넣지 않는다.

### 5. Settings UX

트레이 우클릭 메뉴에 **휴대폰 알림 설정**을 추가한다.

설정 화면은 다음만 제공한다.

- 휴대폰 알림 사용 체크박스
- ntfy topic 입력
- 저장
- 테스트 알림 전송

테스트 전송이 실패해도 기존 Windows 알림과 작업 감지 기능은 영향을 받지 않는다. topic이 비어 있거나 비활성화된 경우 모바일 전송은 조용히 건너뛴다.

### 6. Failure isolation

다음 실패는 모두 Codex/ChatGPT 원 작업과 Windows 팝업을 방해하면 안 된다.

- 인터넷 연결 없음
- DNS/HTTPS 실패
- ntfy 응답 오류
- 잘못된 topic
- 설정 파일 손상/읽기 실패
- 앱 종료 중 진행 중인 모바일 요청

설정 파일이 손상되면 모바일 알림만 비활성 상태로 취급하고 진단 로그에 민감정보 없이 기록한다.

## Test strategy

1. 설정 저장/로드: 활성화와 topic이 설치 폴더 밖 사용자 데이터 경로에서 왕복한다.
2. 선택 정책: `Running`은 전송하지 않고 `ApprovalRequired`/`Completed`만 전송한다.
3. 중복 억제: 같은 `ActivityKey + Status` 반복 이벤트는 한 번만 전송한다.
4. 상태 전이: 같은 턴의 승인 필요 이후 완료는 두 알림 모두 허용한다.
5. 메시지 포맷: Codex/ChatGPT 제목과 본문이 올바르고 비밀값을 포함하지 않는다.
6. 네트워크 실패: 전송 예외가 `HandleActivity()` 또는 Windows 팝업 경로로 전파되지 않는다.
7. UI 회귀: 기존 상단 팝업, 기록, 클릭 원본 복귀 테스트가 그대로 통과한다.
8. 수동 smoke test: 저장된 실제 ntfy topic으로 테스트 알림과 ChatGPT Web 완료 1회, Codex 완료 1회, 승인 필요 1회를 휴대폰에서 확인한다.
9. 기존 Core/Windows 테스트, PowerShell 설치/Hook 통합 테스트, Windows 빌드/게시 패키지 smoke test를 재실행한다.

## Adversarial boundaries

- ntfy 장애가 Codex Hook timeout 또는 Stop JSON 출력을 바꾸지 않는다.
- 브라우저 확장에 topic이나 ntfy 권한을 추가하지 않는다.
- topic을 GitHub, 릴리스 자산, diagnostics.log에 기록하지 않는다.
- `Running`까지 푸시해 알림 피로를 만들지 않는다.
- 중복 완료 이벤트가 여러 휴대폰 알림으로 증폭되지 않는다.
- 앱 업데이트로 사용자 topic 설정을 삭제하지 않는다.
- 모바일 푸시 실패 때문에 Windows 알림을 누락시키지 않는다.

## Scope exclusions

- 자체 모바일 앱 개발
- Firebase Cloud Messaging 직접 연동
- ntfy 자체 서버 운영
- 모바일에서 PC 작업 승인/거절
- 모바일에서 PC 터미널 또는 ChatGPT 탭 원격 실행
- 과거 실패 알림 영구 큐/재전송
- 새 `Error` 또는 일반 `InputRequired` ActivityStatus 추가

`Error`/일반 입력 필요 알림은 현재 이벤트 모델에 해당 상태가 없으므로 별도 기능으로 분리한다. 이번 기능은 사용자의 핵심 요구인 **백그라운드 상태에서도 완료 알림 수신**과 이미 존재하는 **승인 필요 알림**을 먼저 모바일로 확장한다.

## Acceptance criteria

- ChatGPT Web 작업 완료 시 PC가 켜져 있고 트레이 앱이 실행 중이면 휴대폰에 ntfy 푸시가 도착한다.
- Codex 작업 완료와 승인 필요 이벤트도 동일한 휴대폰에 도착한다.
- ChatGPT/Codex 창이 전면에 없어도 동작한다.
- 기존 Windows 상단 팝업과 작업 기록은 이전과 동일하게 동작한다.
- 같은 턴/상태의 중복 이벤트는 휴대폰에 중복 푸시되지 않는다.
- ntfy가 끊겨도 Codex Hook, ChatGPT 브리지, Windows 알림은 정상 동작한다.
- topic은 저장소, 릴리스 패키지, 진단 로그에 포함되지 않는다.
- 앱 업데이트 후에도 사용자 모바일 설정이 유지된다.
