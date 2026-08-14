# Task Recovery and ChatGPT Reconnect

## Purpose

Codex Usage Tray의 복구 기능은 ChatGPT 웹 또는 장시간 모델 작업이 일시 오류로 끊겼을 때 **같은 요청을 무조건 다시 보내는 대신 `RETRY`와 `RESUME`을 구분**한다.

핵심 원칙:

```text
오류/중단 감지
→ 안전한 RETRY인지 상태 확인이 필요한 RESUME인지 분류
→ 완료된 작업은 반복하지 않음
→ 제한된 복구만 수행
→ 복구 여부를 트레이에 기록
```

## ChatGPT 웹 자동 복구

### 안전한 자동 재시도

다음처럼 명시적인 일시 오류가 보이고, **그 오류와 같은 국소 UI 영역 안에** `다시 시도`, `Retry`, `Try again`, `Regenerate` 계열 버튼이 있을 때만 자동 클릭 후보가 된다.

예:

```text
메시지 전송 시간이 초과되었습니다. 다시 시도해 주세요
```

재시도 간격은 다음으로 제한된다.

```text
1회: 3초
2회: 10초
3회: 30초
4회 이상: 자동 재시도 금지
```

예약된 시간 사이에 대화 URL이 바뀌거나 오류 버튼이 사라지면 클릭하지 않고 `복구 필요`로 전환한다. 승인·허용·확인 UI는 자동 승인하지 않는다.

### `연결이 끊어졌습니다. 전체 답변을 기다리는 중입니다`

이 상태는 요청이 이미 서버에 전달되어 응답이 계속 생성 중일 수 있으므로 프롬프트를 다시 전송하지 않는다.

```text
연결 단절 감지
→ 같은 오류 영역에 안전한 Retry 버튼이 있으면 bounded Retry
→ 버튼이 없으면 recovery_required / disconnected_waiting
→ 트레이가 3초 → 10초 → 30초 순서로 원래 탭 재연결 시도
→ 정확히 원래 tab ID가 같은 ChatGPT 대화 URL에 있을 때만 reload
→ 다른 탭을 대신 reload하거나 새 탭을 만들지 않음
→ recovered/completed 신호가 오면 시도 횟수 초기화
```

재연결 횟수는 브라우저 페이지가 아니라 트레이 프로세스가 소유하므로 페이지 reload 때문에 횟수가 초기화되어 무한 반복되는 것을 막는다.

### 장시간 정지

응답 생성 UI가 계속 보이는데 assistant 출력 변화가 180초 이상 없으면 `stalled`로 판정한다. 서버가 실제 처리 중일 가능성이 있으므로 이 경우 **프롬프트 자동 재전송은 하지 않는다.** 트레이에 `복구 필요`로 기록하고 상태 확인 경로로 전환한다.

## 트레이 상태

작업 알림 기록에는 다음 복구 상태가 추가된다.

- `재시도 중` (`Retrying`)
- `복구 필요` (`RecoveryRequired`)
- `자동 복구` (`Recovered`)

`복구 필요`, `자동 복구`, 기존 `승인 필요`, `작업 완료`는 상단 팝업 대상이다. `재시도 중`은 기록에는 남지만 반복 팝업은 만들지 않는다.

## RecoveryRunner

`CodexUsageTray.RecoveryRunner.exe`는 장시간 API 작업을 체크포인트 기반으로 실행하기 위한 **선택적 별도 실행 파일**이다. 트레이 앱 자체는 계속 OpenAI API 키를 사용하지 않는다.

릴리스 설치 위치:

```text
%LOCALAPPDATA%\CodexUsageTray\CodexUsageTray.RecoveryRunner.exe
```

Runner는 자동 시작되지 않으며 사용자가 직접 실행할 때만 `OPENAI_API_KEY` 환경 변수를 읽는다. API 키를 job JSON이나 state JSON에 저장하지 않는다.

### Job 파일

예: `recovery-job.json`

```json
{
  "jobId": "base-20260815-001",
  "model": "gpt-5",
  "prompt": "현재 체크포인트와 승인된 범위를 검토하고 미완료 작업만 계속한다.",
  "maxAttempts": 3,
  "timeoutSeconds": 900
}
```

모델은 job 파일에서 명시하며 Runner 코드가 임의 기본 모델을 선택하지 않는다.

### 새 작업 시작

PowerShell 예시:

```powershell
$env:OPENAI_API_KEY = '<your key>'
cd $env:LOCALAPPDATA\CodexUsageTray
.\CodexUsageTray.RecoveryRunner.exe run --job C:\path\to\recovery-job.json
```

기본 state 파일은 다음 위치에 생성된다.

```text
C:\path\to\recovery-job.json.state.json
```

이미 state 파일이 있으면 `run`은 덮어쓰지 않는다.

### 중단 작업 재개

```powershell
$env:OPENAI_API_KEY = '<your key>'
cd $env:LOCALAPPDATA\CodexUsageTray
.\CodexUsageTray.RecoveryRunner.exe resume --state C:\path\to\recovery-job.json.state.json
```

`Completed` checkpoint이면 새 API 요청을 보내지 않고 기존 결과를 반환한다.

이전 프로세스가 `Running` 상태에서 끊겼거나 POST 결과를 알 수 없는 network/timeout이 발생했다면 `ReconcileRequired`로 종료한다. 이 경우 동일 POST를 자동으로 다시 전송하지 않는다. 중복 API 작업보다 수동 상태 확인을 우선하는 fail-closed 경계다.

명시적인 HTTP `408`, `409`, `429`, `5xx` 응답은 서버가 실패 응답을 반환했다는 증거가 있으므로 job의 `maxAttempts` 범위에서만 3초 → 10초 → 30초 backoff로 재시도할 수 있다. `400` 계열의 비재시도 오류, 인증/입력 오류는 즉시 terminal failure로 기록한다.

## Checkpoint 상태

Runner state에는 최소 다음이 저장된다.

```text
job snapshot
status
attempt
responseId
outputText
lastError
clientRequestId
updatedAt
```

상태 파일은 같은 디렉터리의 임시 파일에 먼저 기록한 뒤 원자적으로 교체한다. 완료된 checkpoint를 재개할 때 API를 다시 호출하지 않는다.

## 보안과 권한 경계

- 트레이 기본 프로세스: API 키 사용 안 함.
- 브라우저 확장: `chatgpt.com`만 접근하며 브라우저 쿠키를 읽지 않는다.
- 자동 승인/자동 허용: 하지 않음.
- 연결단절 재연결: 원래 tab ID + 같은 정규화 대화 URL일 때만 reload.
- RecoveryRunner: 명시 실행 때만 API 키 환경 변수를 읽음.
- API 키, 브라우저 쿠키, Bearer 토큰을 recovery state에 저장하지 않음.
- 결과가 불명확한 POST network failure는 자동 재전송하지 않음.

## 현재 검증 한계

자동 테스트는 DOM 감지 계약, retry/reload 안전성, 트레이 상태, 체크포인트, HTTP 실패 분류와 Windows 빌드를 검증한다. ChatGPT 웹 DOM은 공개 안정 API가 아니므로 실제 화면 구조가 변경되면 감지를 놓칠 수 있다. 이 경우 복구 기능은 임의 버튼을 누르기보다 실패 폐쇄적으로 동작하도록 설계되어 있다.

실제 OpenAI API 비용이 발생하는 live RecoveryRunner 요청은 별도의 사용 가능한 API 키·결제 경로와 사용자의 실제 실행이 있어야 검증할 수 있다. 테스트의 HTTP transport는 로컬 fake handler를 사용한다.
