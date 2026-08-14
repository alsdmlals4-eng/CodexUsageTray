# Resilient Task Recovery Design

## Goal

예기치 않은 ChatGPT 웹 전송/생성 오류나 장시간 정지를 감지하고, 안전한 경우에만 자동 재시도하며, 복구 상태를 Codex Usage Tray에 기록한다. 장시간·중요 작업은 별도 API Runner에서 체크포인트 기반으로 재개할 수 있게 한다.

## Existing-solution-first decision

- `REUSE`: 기존 `browser-extension`의 ChatGPT DOM 감지와 native messaging 경로를 그대로 사용한다.
- `ABSORB`: 새 Watchdog 앱을 만들지 않고 브라우저 감지와 트레이 상태 표시에 복구 상태를 흡수한다.
- `REUSE`: 기존 `ActivityEvent` / `ActivityStore` / popup/history UI를 복구 상태 전달에 확장한다.
- `BUILD_NEW`: API 기반 장시간 실행은 기존 트레이 프로세스와 권한 경계가 다르므로 `CodexUsageTray.RecoveryRunner` 별도 콘솔 앱으로 격리한다.
- 기존 트레이 앱은 계속 OpenAI API 키를 읽지 않는다. API 키는 RecoveryRunner에서만 `OPENAI_API_KEY` 환경 변수로 선택적으로 사용한다.

## Safety boundary

1. 웹 자동 재시도는 **명시적 일시 오류 문구와 같은 오류 컨테이너 안의 재시도 버튼이 동시에 확인된 경우**에만 수행한다.
2. 재시도는 3회로 제한하고 3초 → 10초 → 30초 지연을 사용한다.
3. 단순 장시간 정지는 자동 클릭하지 않는다. `recovery_required`로만 기록해 중복 실행을 막는다.
4. 승인/확인 UI는 기존처럼 자동 승인하지 않는다.
5. API Runner는 외부 부작용을 직접 실행하지 않는다. 모델 요청만 수행하고 체크포인트를 로컬 JSON에 원자적으로 기록한다.
6. API Runner는 408/409/429/5xx 및 네트워크/timeout 계열만 bounded retry 대상으로 분류한다. 인증/권한/잘못된 요청은 즉시 중단한다.
7. API Runner 재시작 시 완료 체크포인트는 재호출하지 않고, `pending` 또는 재시도 가능한 `failed_transient` 작업만 재개한다.

## Browser watchdog

### Detection

- 지원 일시 오류 문구: `메시지 전송 시간이 초과되었습니다`, `message timed out`, `network error`, `something went wrong`, `error generating a response` 계열.
- 재시도 컨트롤: 오류 컨테이너 안에서 `다시 시도`, `retry`, `regenerate`, `try again` 계열 버튼만 허용한다.
- 생성 중 stop 버튼이 보이는 동안 assistant 응답 DOM 변화가 180초 이상 없으면 `stalled`로 기록한다. 자동 클릭은 하지 않는다.

### State

`running → retrying → recovered/completed`

또는

`running → recovery_required`

각 대화별 retry state를 content-script 메모리에 유지하고 route가 바뀌면 초기화한다.

## Tray integration

`ActivityStatus`에 다음을 추가한다.

- `Retrying`
- `RecoveryRequired`
- `Recovered`

웹 parser는 `retrying`, `stalled`, `recovery_required`, `recovered`를 위 상태로 변환한다. 복구 필요와 복구 완료는 popup 대상이며, retrying은 기록에는 남기되 팝업 스팸을 피한다.

History는 진행/승인/복구/완료 카운트를 분리하고 각 상태 라벨을 표시한다.

## RecoveryRunner

새 콘솔 프로젝트 `src/CodexUsageTray.RecoveryRunner`를 만든다.

### CLI

```text
CodexUsageTray.RecoveryRunner.exe run --job <job.json> [--state <state.json>]
CodexUsageTray.RecoveryRunner.exe resume --state <state.json>
```

### Job document

```json
{
  "jobId": "base-20260815-001",
  "model": "gpt-5.6-sol",
  "prompt": "현재 작업 계약과 체크포인트를 검토하고 미완료 작업만 계속한다.",
  "maxAttempts": 3,
  "timeoutSeconds": 900
}
```

### State document

- `jobId`
- `status`: `pending | running | completed | failed_transient | failed_terminal`
- `attempt`
- `responseId`
- `outputText`
- `lastError`
- `updatedAt`

State는 임시 파일에 쓴 뒤 replace/move하여 원자적으로 저장한다.

### API

OpenAI Responses API를 사용한다. 키는 `OPENAI_API_KEY`에서만 읽는다. 모델은 job 문서가 명시하며 기본 모델을 코드에 하드코딩하지 않는다.

## Error handling

- 브라우저 DOM 변화로 selector가 깨진 경우: 자동 클릭 실패는 `recovery_required` 알림으로 downgrade한다.
- native messaging이 끊긴 경우: 기존 background reconnect를 유지한다.
- API key 없음: 명확한 terminal error, API 호출 없음.
- state JSON 손상: 기존 파일을 덮어쓰지 않고 terminal error.
- completed state resume: API 호출 없이 성공 종료.

## Testing

- Node 테스트: transient error 식별, retry button 제한, backoff/attempt ceiling, stall 판단.
- Core 테스트: 새 브라우저 상태 parser 매핑 및 기존 안전 URL 검증 회귀.
- Windows UI 테스트: 복구 상태 popup/history 표현.
- Runner Core 테스트: retry 분류, checkpoint resume, completed short-circuit, atomic state serialization.
- CI에서 `dotnet build CodexUsageTray.sln`과 기존 테스트 전체를 통과해야 한다.

## Rollback

이 기능은 스키마 마이그레이션이나 사용자 데이터 변경이 없다. PR을 revert하면 기존 완료/승인 알림 동작으로 돌아간다. RecoveryRunner state JSON은 독립 파일이라 남아 있어도 기존 앱 동작에 영향을 주지 않는다.
