Codex Usage Tray - ChatGPT 웹 연결

1. Chrome: chrome://extensions 또는 Edge: edge://extensions 를 엽니다.
2. 개발자 모드를 켭니다.
3. "압축해제된 확장 프로그램을 로드"를 누릅니다.
4. 이 browser-extension 폴더를 선택합니다.
5. 열려 있던 chatgpt.com 탭을 새로고침합니다.

확장 프로그램은 chatgpt.com에서 작업 완료/승인 UI를 감지합니다.
로컬 앱으로 보내는 정보는 대화 URL, 탭 제목, 상태뿐입니다.
쿠키와 대화 본문은 읽거나 저장하거나 전송하지 않습니다.
알림을 누르면 감지했던 기존 탭과 창을 앞으로 가져옵니다. 해당 탭이
이미 닫힌 경우에만 같은 대화 URL을 새 탭으로 엽니다.

ChatGPT 웹은 외부 Hook API를 제공하지 않으므로 UI 변경 후 감지가
깨질 수 있습니다. 그 경우 ChatGPT 기본 알림과 Activity 화면을 함께
사용하고 GitHub 저장소에서 최신 버전으로 업데이트하세요.
