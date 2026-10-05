# 애니 오디오 한글자막

Chrome에서 재생하는 영상의 **탭 오디오만** 인식해 한국어 자막을 표시하는 Windows 프로그램과 Chrome 확장 프로그램입니다.

이 프로젝트는 다음 조건을 기준으로 설계했습니다.

1. Windows `.exe`가 본체입니다.
2. Chrome 확장 프로그램은 탭 오디오 연결과 브라우저 자막 표시만 담당합니다.
3. 자막은 영상의 **소리**를 음성 인식한 뒤 한국어로 표시합니다.
4. 화면 캡처·OCR·사이트 자막 탐색·에피소드별 자막 파일 선택을 하지 않습니다.
5. 정식 공개본은 공개 소스 빌드에서 받은 Authenticode 서명본만 배포합니다.

## 동작 구조

| 구성 | 하는 일 | 하지 않는 일 |
| --- | --- | --- |
| `AnimeAudioCaptioner.exe` | 일본어/영어 탭 음성을 로컬 Whisper 모델로 텍스트화 | 화면 캡처, 사이트 검사, PowerShell 실행, 프로세스 주입 |
| Chrome `애니 오디오 브리지` | 현재 탭의 PCM 오디오를 `127.0.0.1`로 전달하고 한국어 오버레이 표시 | 영상 픽셀 OCR, 사이트 자막 탐색 |
| Chrome 내장 Translator | 일본어/영어 텍스트를 한국어로 번역 | 영상·화면 업로드 |

영상은 Chrome 창 모드에서 평소처럼 재생한다. 전체화면과 에피소드별 자막 파일 선택은 필요 없다. Chrome 보안 규칙상 새 탭에서 음성 자막을 시작할 때만 확장 아이콘을 한 번 누른다.

## 서명 배포 방식

Microsoft Partner Center 없이도 공개 오픈소스 프로젝트는 SignPath Foundation의 정식 Authenticode 서명 심사를 신청할 수 있습니다. 이 저장소는 그 절차를 위한 공개 라이선스, 재현 가능한 GitHub Actions 빌드, 서명 대상 설정을 포함합니다.

- 공개 전에는 서명되지 않은 `.exe`를 사용자용 설치본으로 배포하지 않습니다.
- Defender 또는 SmartScreen을 우회하거나 예외 추가를 요구하지 않습니다.
- 서명 승인 뒤 GitHub의 태그 빌드가 원본 소스에서 `.exe`를 만들고 서명 요청을 보냅니다.
- 설정 방법은 [SIGNPATH.md](SIGNPATH.md)에 있습니다.

Chrome 확장 프로그램은 별도로 Chrome Web Store에 게시해야 일반 사용자 설치가 가능합니다. 개발자 모드 압축 해제 설치는 검사용으로만 사용합니다.

## 검증된 범위

- `src/AnimeAudioCaptioner`는 `win-x64` Windows GUI `.exe`로 컴파일했다.
- `chrome-extension/manifest.json` JSON 및 모든 JavaScript 문법을 검사했다.
- 실제 Windows/Chrome 탭 오디오 테스트와 서명 후 Defender·SmartScreen 검증은 서명 승인 후 깨끗한 Windows 환경에서 진행해야 합니다.

## 구성

```text
src/AnimeAudioCaptioner/       Windows WPF 본체 (.NET 8 / Whisper.net)
chrome-extension/              Chrome 오디오 브리지
store/                         서명·Store 배포 체크리스트
```

## 개발용 실행

Windows 11 x64와 .NET 8 SDK에서 다음 프로젝트를 빌드한다.

```powershell
dotnet build src/AnimeAudioCaptioner/AnimeAudioCaptioner.csproj -c Release -r win-x64
dotnet publish src/AnimeAudioCaptioner/AnimeAudioCaptioner.csproj -c Release -r win-x64 --self-contained true
```

첫 실행에는 다국어 Whisper Base 모델(약 148MB)을 한 번 내려받는다. 일본어 음성이 기본이며, 영어 더빙은 확장 옵션에서 한 번 바꾸면 된다.

`bin/`의 서명 전 산출물은 개발·검사용일 뿐이며 사용자에게 배포하면 안 됩니다. 공개용 파일은 서명된 GitHub Actions 산출물만 사용합니다.

## 라이선스와 구성 요소

이 프로젝트의 소스는 [MIT License](LICENSE)로 공개합니다. 직접 사용하는 `Whisper.net` 및 `Whisper.net.Runtime` 1.9.1도 MIT 라이선스입니다. 자세한 표기는 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)를 참고하세요.
