# Codex Usage Tray

Windows 오른쪽 아래 알림 영역에서 **Codex 잔여 사용량**을 숫자로 확인하는 가벼운 트레이 앱입니다.

로컬 Codex 작업이 승인을 기다리거나 끝났을 때 어느 프로젝트·채팅·터미널인지 Windows 팝업으로 알려줍니다.

## 표시 내용

- 가장 적게 남은 Codex 제한 구간을 트레이 아이콘 숫자로 표시
- 초록: 51~100%, 노랑: 20~50%, 빨강: 0~19%
- 아이콘 왼쪽 클릭: 5시간·주간 등 모든 제한 구간과 초기화 시각 표시
- 아이콘 오른쪽 클릭: 상세 보기, 수동 새로고침, Codex 로그인, 자동 시작, 종료
- 5분마다 자동 새로고침
- 로컬 Codex 새 요청을 `진행 중`으로 기록
- 승인 대기: 프로젝트, 채팅, 도구·명령 요약을 상단 대형 경고 팝업으로 표시
- 작업 완료: 프로젝트, 채팅, 마지막 응답 요약을 상단 대형 완료 팝업으로 표시
- 팝업은 항상 위에 유지되고 클릭 전까지 사라지지 않으며 여러 알림을 순서대로 보존
- 팝업 클릭: 감지된 원본 PowerShell/Windows Terminal 창 또는 ChatGPT 웹 대화 열기
- 최근 50개 턴의 `진행 중 / 승인 필요 / 완료` 상태 기록

앱은 OpenAI API 키나 브라우저 쿠키를 사용하지 않습니다. 공식 Codex App Server의 `account/rateLimits/read`를 호출하고, Codex CLI가 보관하는 기존 로그인만 재사용합니다.

작업 알림은 공식 Codex `UserPromptSubmit`, `PermissionRequest`, `Stop` Hook을 사용합니다. 승인 Hook은 **알림만 전송하며 자동 승인이나 자동 거절을 하지 않습니다.**

## 준비물

실행에 필요한 것:

1. Windows 10/11 x64
2. Codex CLI가 설치되어 있고 PowerShell에서 `codex --version`이 실행될 것
3. `codex login`으로 ChatGPT 계정에 로그인되어 있을 것

소스에서 빌드할 때만 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)가 추가로 필요합니다. 만들어진 `.exe`는 .NET 런타임을 자체 포함합니다.

## 가장 쉬운 설치 방법

Windows PowerShell을 열고 아래 한 줄을 그대로 붙여넣으세요.

```powershell
irm https://raw.githubusercontent.com/alsdmlals4-eng/CodexUsageTray/main/install-online.ps1 | iex
```

명령 한 줄이 최신 Windows x64 릴리스 다운로드, SHA-256 검증, 압축 해제, 설치, Windows 자동 시작 등록, 앱 실행까지 처리합니다. 업데이트할 때도 같은 명령을 다시 실행하면 됩니다.

설치 스크립트를 먼저 확인하고 실행하려면 다음 명령을 차례로 사용하세요.

```powershell
$installer = Join-Path $env:TEMP 'install-codex-usage-tray.ps1'
Invoke-WebRequest https://raw.githubusercontent.com/alsdmlals4-eng/CodexUsageTray/main/install-online.ps1 -OutFile $installer -UseBasicParsing
notepad $installer
powershell.exe -NoProfile -ExecutionPolicy Bypass -File $installer
```

설치 위치는 다음과 같습니다.

```text
%LOCALAPPDATA%\CodexUsageTray\CodexUsageTray.exe
```

처음 실행할 때 Windows SmartScreen이 나타날 수 있습니다. 이 프로젝트는 아직 코드 서명 인증서를 사용하지 않으므로, 실행 전 [공개 소스](https://github.com/alsdmlals4-eng/CodexUsageTray)와 [릴리스 체크섬](https://github.com/alsdmlals4-eng/CodexUsageTray/releases/latest)을 확인할 수 있습니다.

설치 후 Codex를 다시 시작하고 다음 절차를 한 번 수행해야 합니다.

1. Codex CLI에서 `/hooks`를 입력합니다.
2. `~/.codex/hooks.json`의 Codex Usage Tray Hook 세 개를 검토합니다.
3. `UserPromptSubmit`, `PermissionRequest`, `Stop` 항목을 신뢰합니다.

Codex는 변경된 Hook을 사용자가 직접 검토·신뢰하기 전까지 실행하지 않습니다.

기존 사용자 Hook이 있으면 설치 스크립트가 먼저 `hooks.json.backup-날짜-시각` 파일을 만든 뒤 앱 항목만 병합합니다. 기존 Hook을 덮어쓰지 않습니다.

## 빌드만 하기

소스에서 직접 빌드하려는 개발자는 저장소를 내려받은 폴더에서 다음 명령을 실행하세요.

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\scripts\build-windows.ps1
```

성공한 실행 파일:

```text
artifacts\win-x64\CodexUsageTray.exe
artifacts\win-x64\CodexUsageTray.EventBridge.exe
```

## 사용 방법

- 숫자 아이콘을 왼쪽 클릭하면 상세 창이 열립니다.
- 아이콘을 오른쪽 클릭하고 **새로고침**을 누르면 즉시 다시 확인합니다.
- **Windows 시작 시 실행**을 선택하거나 해제하면 현재 사용자에게만 적용됩니다.
- 회색 `?`는 Codex CLI를 찾지 못했다는 뜻입니다.
- 회색 `!`는 로그인 또는 Codex App Server 연결 확인이 필요하다는 뜻입니다.
- **작업 알림 기록**에서 진행 중·승인 대기·완료 턴을 확인할 수 있습니다.
- 승인·완료 팝업을 클릭하면 원본 터미널을 찾을 수 있을 때 해당 창을 앞으로 가져옵니다.

## ChatGPT 웹 알림 연결

설치 파일에는 Chrome/Edge용 최소 권한 확장이 포함됩니다. 브라우저 보안 정책상
사용자가 확장을 한 번 직접 로드해야 합니다.

1. 트레이 아이콘 우클릭 → **웹 ChatGPT 확장 폴더 열기**를 누릅니다.
2. Chrome은 `chrome://extensions`, Edge는 `edge://extensions`를 엽니다.
3. **개발자 모드**를 켜고 **압축해제된 확장 프로그램을 로드**를 누릅니다.
4. 열린 `%LOCALAPPDATA%\CodexUsageTray\browser-extension` 폴더를 선택합니다.
5. 이미 열려 있던 `chatgpt.com` 탭을 새로고침합니다.

이후 ChatGPT 웹 응답 생성이 안정적으로 끝나거나 승인/확인 UI가 감지되면 같은 상단
팝업이 나타납니다. 팝업을 누르면 감지했던 기존 브라우저 탭과 창을 그대로 앞으로
가져오고 알림을 자동 제거합니다. 원래 탭이 이미 닫힌 경우에만 같은 대화 URL을 새
탭으로 엽니다.

확장은 `chatgpt.com`에서만 실행하며 대화 URL, 탭 제목, 완료/승인 상태만 로컬
브리지로 보냅니다. 쿠키와 대화 본문은 읽거나 저장하거나 전송하지 않습니다.

ChatGPT 웹에는 외부 작업 완료 Hook이 공개되어 있지 않으므로 이 감지는 화면 UI에
의존하는 최선형 기능입니다. ChatGPT 화면 구조가 바뀌면 일시적으로 놓칠 수 있어
ChatGPT **설정 → 알림**에서 턴 완료 및 권한/질문 기본 알림도 함께 켜 두는 것을
권장합니다. 트레이 메뉴의 **ChatGPT 알림 설정 안내**에서 공식 안내를 열 수 있습니다.

## 문제 해결

### `Codex CLI가 설치되어 있지 않음`이 표시될 때

PowerShell에서 아래 두 명령을 확인하세요.

```powershell
codex --version
codex login
```

첫 명령이 인식되지 않으면 Codex CLI를 설치한 뒤 Windows에서 로그아웃하고 다시 로그인하거나 PC를 재시작해 PATH를 갱신하세요.

### 사용량이 갱신되지 않을 때

1. 아이콘 우클릭 → **Codex 로그인**을 실행합니다.
2. 로그인이 끝나면 아이콘 우클릭 → **새로고침**을 누릅니다.
3. 계속 실패하면 아이콘 우클릭 → **진단 로그 폴더 열기**를 누릅니다.
4. `%LOCALAPPDATA%\CodexUsageTray\diagnostics.log`에서 실제 App Server 오류를 확인합니다.

진단 로그에는 App Server의 최근 stderr와 오류 종류만 기록합니다. 로그는 128KiB로
제한되고 Bearer 토큰, `accessToken`, `refreshToken`, `apiKey` 형태의 값은 자동으로
제거됩니다. 프롬프트, Codex 응답, Hook 입력, 환경 변수는 기록하지 않습니다.

### 승인·완료 팝업이 나타나지 않을 때

1. Codex를 완전히 종료했다가 다시 실행합니다.
2. `/hooks`를 열어 세 Hook이 `신뢰됨` 상태인지 확인합니다.
3. `%USERPROFILE%\.codex\hooks.json`에 `invoke-codex-hook.cmd`가 있고 `timeout`이 `15`인지 확인합니다.
4. 트레이 앱 우클릭 → **작업 알림 기록**을 확인합니다.

`v1.2.6`부터 트레이 프로세스가 살아 있는 상태에서 내부 작업 알림 수신기가 예기치
않게 실패해도 수신기를 자동 복구하고, 실패 원인을 `diagnostics.log`에 기록합니다.
Codex와 웹 ChatGPT 알림이 동시에 멈췄다면 최신 버전을 다시 설치한 뒤 두 경로를
각각 한 번 테스트하세요.

Codex에 `Stop hook (failed): hook returned invalid stop hook JSON output`이 표시되면
`v1.2.6` 이하를 최신 버전으로 다시 설치하고 Codex를 완전히 재시작한 뒤 `/hooks`에서
변경된 Hook을 다시 신뢰하세요. `v1.2.7`부터 Stop Hook은 Codex가 요구하는
`{"continue":true}` JSON을 정확히 반환하며, 알림 전달 실패가 Codex 턴 결과를
변경하지 않습니다. 정상 상태에서는 짧은 작업이 끝날 때 `Stop hook (completed)`가
표시되고 화면 상단에 완료 팝업이 나타납니다.

웹 ChatGPT만 감지되지 않으면 확장 관리 화면에서 확장이 켜져 있는지 확인하고
`chatgpt.com` 탭을 새로고침하세요. 확장 오류에 `Native host has exited`가 보이면
한 줄 설치 명령을 다시 실행해 네이티브 연결을 재등록합니다.

확장 오류에 `Cannot read properties of undefined (reading 'sendMessage')`가 보이면
확장을 새로고침하기 전에 열려 있던 ChatGPT 탭의 확장 컨텍스트가 만료된 것입니다.
오류 화면의 **모두 삭제**는 기록만 지우므로 해당 ChatGPT 탭에서 `Ctrl+Shift+R`을
누르세요. `v1.2.5`부터 만료된 탭의 감시를 안전하게 중단해 같은 오류가 반복해서
쌓이지 않으며, 탭을 새로고침하면 웹 완료·승인 알림이 다시 연결됩니다.

Hook을 다시 병합하려면 설치 폴더에서 다음 명령을 실행합니다.

```powershell
.\setup-integration.ps1 -BridgePath .\CodexUsageTray.EventBridge.exe
```

### 업데이트

가장 쉬운 설치 방법의 한 줄 명령을 다시 실행하세요. 실행 중인 앱을 안전하게 종료하고 새 버전으로 교체한 뒤 다시 실행합니다. Hook 정의가 바뀌었다면 Codex를 다시 시작한 뒤 `/hooks`에서 다시 검토·신뢰합니다. 웹 확장 코드가 바뀐 버전은 `chrome://extensions` 또는 `edge://extensions`의 확장 카드에서 **새로고침**을 누른 뒤 열려 있던 모든 ChatGPT 탭도 한 번 새로고침합니다.

`v1.2.2` 이하에서 업데이트 중 `파일이 다른 프로세스에서 사용되고 있으므로`라는
`Move-Item` 오류가 나타나면 웹 ChatGPT 확장의 EventBridge가 설치 폴더를 사용 중인
경우입니다. `v1.2.3`부터는 새 패키지를 먼저 준비한 뒤 설치 경로의 트레이와
EventBridge만 종료하고 교체하므로 Chrome/Edge를 직접 종료할 필요가 없습니다. 이
오류로 중단된 뒤에는 최신 한 줄 설치 명령을 다시 실행하세요.

### 앱 제거

1. 아래 명령으로 Codex Hook과 Windows 자동 시작 등록을 제거합니다.

   ```powershell
   & "$env:LOCALAPPDATA\CodexUsageTray\remove-integration.ps1"
   ```

2. 아이콘 우클릭 → **종료**를 선택합니다.
3. `%LOCALAPPDATA%\CodexUsageTray` 폴더를 삭제합니다.

앱 제거는 Codex 로그인이나 Codex 설정을 삭제하지 않습니다.

## Windows 수동 검증 목록

빌드 후 다음 항목을 한 번씩 확인하세요.

- [ ] `CodexUsageTray.exe` 실행 시 일반 창 없이 트레이 아이콘이 나타난다.
- [ ] 실제 `/status` 또는 Codex 사용량 화면과 트레이의 잔여율이 일치한다.
- [ ] 왼쪽 클릭 상세 창에 5시간·주간 구간과 현지 초기화 시각이 보인다.
- [ ] 우클릭 **새로고침**이 정상 동작한다.
- [ ] `codex logout` 뒤 `!` 안내가 나타나고 다시 로그인하면 복구된다.
- [ ] **Windows 시작 시 실행**을 켠 뒤 재로그인하면 앱이 자동 실행된다.
- [ ] 앱을 두 번 실행해도 트레이 아이콘이 하나만 나타난다.
- [ ] 종료 후 `codex app-server` 자식 프로세스가 남지 않는다.
- [ ] 새 로컬 Codex 요청을 보내면 작업 기록에 `진행 중`이 나타난다.
- [ ] 승인이 필요한 명령에서 프로젝트·채팅이 포함된 `승인 요청` 팝업이 화면 상단 중앙에 크게 나타난다.
- [ ] 팝업이 다른 앱보다 위에 있고 클릭하기 전까지 사라지지 않는다.
- [ ] 팝업이 승인 여부를 자동으로 바꾸지 않고 원래 Codex 승인 화면이 유지된다.
- [ ] 턴이 끝나면 마지막 응답이 포함된 `작업 완료` 팝업이 나타난다.
- [ ] Codex 0.147.0에서 턴 종료 후 `Stop hook (completed)`가 표시되고 invalid JSON 오류가 없다.
- [ ] 팝업 클릭 시 원본 터미널을 찾으면 앞으로 가져오고, 못 찾으면 기록 창이 열린다.
- [ ] ChatGPT 웹 확장 로드 후 응답이 실제로 끝난 다음에만 완료 팝업이 나타난다.
- [ ] ChatGPT 웹 완료 팝업을 누르면 새 탭 수가 늘지 않고 감지했던 기존 탭이 앞으로 온다.
- [ ] 기존 사용자 Hook이 설치·제거 전후 그대로 남는다.

## 개발 구조

```text
src/CodexUsageTray.Core/       JSON-RPC, 응답 파서, 잔여율 계산
src/CodexUsageTray/            WinForms 트레이 UI
src/CodexUsageTray.EventBridge/ Codex Hook → 트레이 IPC 브리지
browser-extension/             ChatGPT 웹 상태 → 네이티브 브리지
tests/CodexUsageTray.Core.Tests/ 외부 패키지 없는 테스트 실행기
scripts/                       Windows 빌드·설치 스크립트
```

공식 근거: [Codex App Server](https://learn.chatgpt.com/docs/app-server), [Codex Hooks](https://learn.chatgpt.com/docs/hooks), [ChatGPT 알림](https://learn.chatgpt.com/docs/notifications)
