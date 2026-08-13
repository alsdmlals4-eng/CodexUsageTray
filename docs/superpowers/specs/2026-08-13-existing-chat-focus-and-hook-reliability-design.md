# Existing Chat Focus and Hook Reliability Design

## Goal

Codex Usage Tray 알림을 클릭했을 때 이미 열려 있는 정확한 ChatGPT 탭이나 Codex 터미널로 즉시 복귀한다. 기존 ChatGPT 탭이 닫힌 경우에만 저장된 URL을 새 탭으로 연다. Codex Hook은 알림 브리지의 지연이나 실패 때문에 Codex 턴을 실패 상태로 표시하지 않는다.

## Confirmed Problems

### ChatGPT web

현재 `ActivitySourceLauncher`는 저장된 URL을 `Process.Start`로 Windows 기본 브라우저에 전달한다. 이 경로에는 원래 브라우저 창과 탭의 신원이 없으므로 Chrome이 같은 대화의 새 탭을 만든다.

현재 콘텐츠 스크립트는 Stop 버튼이 보였다가 사라지는 첫 전환을 즉시 완료로 처리한다. ChatGPT가 답변 시작 과정에서 composer와 응답 영역을 재배치할 때 Stop 버튼이 잠시 사라질 수 있으므로, 실제 응답이 계속 생성 중인데도 완료 알림이 먼저 발생한다.

### Codex Hook

설치된 Hook 제한 시간은 3초다. 자체 포함 EventBridge의 최초 시작과 트레이 named-pipe 재연결에는 최대 약 2.1초의 애플리케이션 재시도 시간에 프로세스 시작 시간이 추가된다. 직접 EventBridge 실행은 종료 코드 0이지만 Codex Hook 실행에서 코드 1이 보고된 증거와 비교하면 3초 제한은 안전 여유가 없다. 또한 현재 Hook 명령은 EventBridge 프로세스를 직접 실행하므로 예기치 않은 브리지 종료 코드가 Codex에 그대로 노출된다.

## Selected Architecture

### Persistent browser connection

브라우저 확장 background service worker는 one-shot `sendNativeMessage` 대신 `connectNative` 포트를 유지한다. 네이티브 EventBridge는 연결마다 임의의 `BrowserConnectionId`를 생성하고 같은 ID를 포함한 로컬 명령 pipe를 연다.

콘텐츠 스크립트가 완료 또는 승인 이벤트를 보내면 background가 신뢰 가능한 `sender.tab.id`와 `sender.tab.windowId`를 붙인다. EventBridge는 이 메타데이터와 연결 ID를 `ActivityEvent`에 추가해 트레이로 보낸다. 대화 본문과 쿠키는 계속 수집하지 않는다.

### Existing-tab activation

웹 알림 클릭 시 트레이는 해당 이벤트의 연결 ID를 사용해 정확한 네이티브 호스트 pipe에 `activate` 명령을 보낸다. 확장은 다음 순서로 처리한다.

1. 저장된 탭 ID가 아직 같은 정규화 ChatGPT URL을 가리키면 해당 탭과 창을 활성화한다.
2. 탭 ID가 바뀌었으면 열린 `chatgpt.com` 탭 중 같은 정규화 URL을 찾고 활성화한다.
3. 일치하는 탭이 없을 때만 저장된 URL을 새 탭으로 연다.
4. 네이티브 연결 자체가 끊겼으면 트레이가 기존 Windows URL 실행을 최후 수단으로 사용한다.

탭 활성화는 페이지를 새로고침하지 않으며 새 채팅을 만들지 않는다. 알림은 클릭 시 현재 동작대로 큐에서 제거한다.

### Stable completion detection

콘텐츠 스크립트는 boolean 한 개 대신 생성 상태 머신을 사용한다.

1. Stop/streaming 신호를 실제로 관측해야 `running` 상태에 들어간다.
2. Stop 신호가 사라지면 즉시 알리지 않고 완료 후보 시각을 기록한다.
3. 후보 기간 중 Stop 신호가 다시 나타나거나 최신 assistant 응답 영역에 DOM 변화가 생기면 후보를 취소한다.
4. Stop 신호 부재와 assistant 응답 영역 무변화가 모두 2초 이상 지속될 때 한 번만 완료 이벤트를 보낸다.

응답 텍스트는 읽거나 복사하지 않는다. MutationObserver가 제공하는 변경 대상이 최신 assistant 응답 영역에 속하는지만 확인하고 변경 시각만 메모리에 유지한다. 페이지 이동이나 새 요청이 시작되면 이전 후보 timer를 취소한다.

### Hook failure boundary

설치 스크립트는 사용자 설치 폴더에 ASCII `invoke-codex-hook.cmd`를 생성한다. 이 래퍼는 stdin을 EventBridge에 그대로 전달하고 EventBridge 결과와 관계없이 `exit /b 0`으로 끝난다. Hook 제한 시간은 3초에서 15초로 늘려 자체 포함 실행 파일의 cold start와 pipe 재연결에 여유를 둔다.

Stop Hook의 `{}` stdout은 EventBridge가 그대로 출력한다. 래퍼는 추가 stdout을 만들지 않는다. 알림 실패는 Codex 승인 또는 턴 결과를 변경하지 않는다.

## Data and Security Constraints

- 확장 host 권한은 `https://chatgpt.com/*` 하나로 유지한다.
- 브라우저 메타데이터는 탭 ID, 창 ID, 로컬 연결 ID, 대화 URL, 탭 제목, 상태로 제한한다.
- 탭 활성화 전 URL의 scheme과 host를 다시 검증한다.
- 연결 ID는 pipe 이름에 사용할 수 있는 GUID 형식만 허용한다.
- named pipe는 현재 Windows 사용자 세션의 로컬 통신에만 사용한다.
- 자동 승인, 자동 거절, 명령 실행은 추가하지 않는다.

## Failure Handling

- 저장된 탭이 다른 URL로 이동했으면 같은 대화 URL의 다른 탭을 검색한다.
- 같은 대화 탭이 없으면 새 탭을 한 번만 생성한다.
- 네이티브 포트가 종료되면 확장은 지연 후 다시 연결한다.
- 브라우저가 닫혔거나 명령 pipe가 없으면 트레이는 URL 실행으로 폴백한다.
- Hook 래퍼는 EventBridge 실패를 Codex에 전파하지 않는다.

## Verification Contract

- 순수 JavaScript 테스트: 선호 탭 ID 일치, 다른 동일 URL 탭 일치, 일치 없음의 세 분기.
- 완료 상태 머신 테스트: 시작 직후 Stop 버튼 일시 소실은 알리지 않음, 재등장 시 후보 취소, 2초 안정 상태에서 한 번만 완료, 페이지 이동 시 이전 후보 폐기.
- Core 테스트: 안전한 웹 메타데이터 파싱, 연결 ID 검증, IPC JSON round trip.
- PowerShell 5.1 테스트: 래퍼 생성, Hook 명령 병합, 15초 timeout, 기존 사용자 Hook 보존.
- Windows UI 테스트: 웹 이벤트 클릭이 브라우저 명령 전송 성공 시 기록 창을 열지 않음.
- CI: 확장 manifest/JavaScript 검사, Core/UI/PowerShell 테스트, tray와 EventBridge 전체 빌드, 릴리스 스모크 설치.
- 수동 Windows 확인: 기존 ChatGPT 탭을 열어 둔 상태에서 알림을 클릭하면 탭 수가 늘지 않고 원래 창·탭이 활성화됨.

## Rollback

v1.2.1 문제가 발생하면 v1.2.0 릴리스의 ZIP을 다시 설치할 수 있다. Hook 설치는 기존 `hooks.json.backup-날짜-시각` 백업을 유지하며, 확장 변경은 `chrome://extensions`의 새로고침 또는 v1.2.0 폴더 재설치로 되돌릴 수 있다.
