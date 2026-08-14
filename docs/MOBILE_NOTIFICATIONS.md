# 휴대폰 ntfy 알림

Codex Usage Tray가 이미 감지하는 **Codex 터미널**과 **ChatGPT 웹**의 작업 완료/승인 필요 이벤트를 기존 Windows 알림과 함께 휴대폰으로 전달한다.

## 동작 조건

- Windows PC가 켜져 있고 `CodexUsageTray.exe`가 실행 중이어야 한다.
- Codex 터미널이나 ChatGPT 브라우저 탭은 전면에 있을 필요가 없다.
- 휴대폰에는 ntfy 앱이 설치되어 있고 해당 topic을 구독해야 한다.
- 휴대폰 OS에서 ntfy 알림 권한과 백그라운드 알림을 허용해야 한다.

## 설정

1. 휴대폰 ntfy 앱에서 추측하기 어려운 전용 topic을 구독한다.
2. Windows 알림 영역의 **Codex Usage Tray** 아이콘을 우클릭한다.
3. **휴대폰 알림 설정**을 연다.
4. 휴대폰에서 구독한 ntfy topic을 입력한다.
5. **휴대폰 알림 사용**을 체크한다.
6. **저장**을 누른다.
7. **테스트 알림**을 눌러 휴대폰 수신을 확인한다.

Topic은 영문 대소문자, 숫자, `-`, `_`만 허용하며 최대 64자다. 설정창에서는 topic을 암호 입력처럼 가려서 표시한다.

## 어떤 이벤트가 휴대폰으로 가는가

휴대폰 Push 대상:

- `ApprovalRequired`: 승인 필요
- `Completed`: 작업 완료

다음 상태는 모바일 완료/승인 기능에서 보내지 않는다.

- `Running`
- `Retrying`
- `RecoveryRequired`
- `Recovered`

복구 상태는 기존 복구 시스템과 Windows 기록/팝업 정책을 그대로 사용한다. 모바일 알림 기능이 복구 정책을 변경하지 않는다.

## 전송 내용

ChatGPT Web:

- ChatGPT 출처
- 채팅 제목/라벨
- 기존 ActivityEvent의 요약

Codex:

- Codex 출처
- 프로젝트명
- 채팅 라벨
- 기존 ActivityEvent의 요약

전송하지 않는 항목:

- 대화 본문 전체
- ActivityEvent의 `Detail`
- Hook 원문
- 브라우저 쿠키
- OpenAI 로그인/API 토큰
- 환경 변수

## 설정 저장 위치

사용자 설정은 설치 폴더와 분리해 다음 위치에 보관한다.

```text
%LOCALAPPDATA%\CodexUsageTrayData\mobile-notifications.json
```

Codex Usage Tray 업데이트가 `%LOCALAPPDATA%\CodexUsageTray`를 교체해도 모바일 topic 설정은 유지된다.

## 장애 처리

- ntfy 또는 인터넷 연결이 실패해도 Codex/ChatGPT 원 작업을 중단하지 않는다.
- 기존 Windows 작업 기록과 팝업은 계속 동작한다.
- 모바일 전송은 UI 스레드를 기다리지 않는 비동기 경로로 처리한다.
- 모바일 전송 오류는 topic 또는 요청 본문 없이 진단 로그에 실패 종류만 기록한다.
- 첫 버전에는 영구 재시도 큐가 없다. 오프라인 중 놓친 과거 Push를 나중에 대량 재전송하지 않는다.

## 중복 방지

같은 `ActivityKey + Status`는 앱 실행 중 한 번만 휴대폰으로 보낸다. 같은 작업이라도 `ApprovalRequired -> Completed`처럼 상태가 실제로 바뀌면 각각 한 번씩 보낼 수 있다.

## 범위 밖

이번 기능은 다음을 제공하지 않는다.

- 휴대폰에서 PC 승인/거절
- 휴대폰에서 터미널 또는 ChatGPT 탭 원격 제어
- 자체 모바일 앱
- Firebase 직접 연동
- 자체 ntfy 서버 운영
- 과거 알림 영구 큐
