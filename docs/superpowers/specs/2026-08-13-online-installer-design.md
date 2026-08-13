# Codex Usage Tray 온라인 설치·배포 설계

## 목표

사용자가 Windows PowerShell 5.1에서 한 줄만 실행하면 Codex Usage Tray 최신 안정 버전을 내려받고, 무결성을 확인하고, 설치·통합·자동 시작·실행까지 완료한다.

```powershell
irm https://raw.githubusercontent.com/alsdmlals4-eng/CodexUsageTray/main/install-online.ps1 | iex
```

## 배포 위치와 비용

- 공개 GitHub 저장소: `alsdmlals4-eng/CodexUsageTray`
- 실행 파일 배포: GitHub Release 자산
- 빌드: 공개 저장소의 표준 `windows-latest` GitHub Actions 러너
- 추가 유료 서비스와 API 키는 사용하지 않는다.
- 저장소는 공개되지만 별도 라이선스는 사용자가 결정하기 전까지 추가하지 않는다.

## Release 생성

`.github/workflows/release.yml`은 `v*` 태그와 수동 실행에서 다음 순서로 동작한다.

1. 저장소 체크아웃 및 .NET 8 설정
2. 핵심 테스트 실행
3. `win-x64` self-contained 단일 파일로 트레이 앱과 Event Bridge 게시
4. 배포 전용 설치 스크립트와 제거 스크립트를 함께 ZIP으로 패키징
5. ZIP의 SHA-256 체크섬 파일 생성
6. 같은 태그의 GitHub Release에 ZIP과 체크섬 업로드

테스트나 게시 단계가 실패하면 Release를 만들지 않는다.

## 온라인 설치 흐름

`install-online.ps1`은 외부 매개변수 없이 실행 가능한 UTF-8 BOM 스크립트다.

1. TLS 1.2 이상을 활성화한다.
2. GitHub의 `releases/latest/download` 고정 경로에서 ZIP과 SHA-256 파일을 임시 폴더로 받는다.
3. `Get-FileHash -Algorithm SHA256` 결과가 체크섬과 정확히 일치하는지 확인한다.
4. 불일치하면 설치 파일을 건드리지 않고 중단한다.
5. 기존 설치 경로의 `CodexUsageTray.exe`만 식별해 실행 중이면 종료한다.
6. Release ZIP을 임시 폴더에 풀고 `%LOCALAPPDATA%\CodexUsageTray`에 원자적으로 교체 가능한 파일 단위로 복사한다.
7. 기존 `hooks.json`을 백업하고 앱 Handler만 병합한다.
8. 현재 사용자 자동 시작을 등록한다.
9. 트레이 앱을 실행하고 임시 폴더를 정리한다.

## 안전성과 보호 대상

- 다운로드 출처는 `alsdmlals4-eng/CodexUsageTray`의 HTTPS Release로 고정한다.
- 체크섬이 없거나 형식이 잘못됐거나 일치하지 않으면 실패 처리한다.
- 다른 프로세스와 다른 Codex Hook은 종료·삭제하지 않는다.
- 기존 `hooks.json`은 수정 전에 시간표시 백업을 만든다.
- Hook은 승인 요청을 자동 승인·거부하지 않는다.
- 관리자 권한을 요구하지 않고 현재 사용자 범위만 수정한다.
- 설치 실패 시 기존 설치 파일을 가능한 한 유지하고 오류를 출력한다.

## 업데이트와 롤백

- 같은 한 줄 명령을 다시 실행하면 최신 Release로 업데이트한다.
- 제거는 설치 폴더의 `remove-integration.ps1`로 Hook과 자동 시작을 제거한 뒤 설치 폴더를 삭제한다.
- 특정 과거 버전으로 되돌릴 때는 해당 Release ZIP을 내려받아 동일한 배포 설치 스크립트를 실행할 수 있다.

## 제한사항

- 코드 서명 인증서가 없으므로 최초 실행 시 Windows SmartScreen 경고가 나타날 수 있다.
- 정확한 웹 ChatGPT 채팅 상태는 공개 외부 인터페이스가 없어 ChatGPT 네이티브 알림을 계속 사용한다.
- `irm | iex`는 공개 원격 스크립트를 즉시 실행한다. 저장소에서 스크립트 내용을 먼저 검토할 수 있도록 README에 원문 링크와 단계별 대체 명령을 함께 제공한다.

## 완료 기준

- 공개 저장소의 기본 브랜치에서 온라인 설치 스크립트를 열람할 수 있다.
- GitHub Actions가 테스트와 Windows 게시를 통과한 Release를 생성한다.
- 깨끗한 Windows 10/11 x64 환경에서 한 줄 명령으로 설치·Hook 병합·자동 시작·트레이 실행이 완료된다.
- 같은 명령을 다시 실행해 업데이트할 수 있다.
- 체크섬 변조 테스트에서 설치가 중단되고 기존 설치가 보존된다.
- 기존 사용자 Hook이 설치·업데이트·제거 전후 유지된다.
