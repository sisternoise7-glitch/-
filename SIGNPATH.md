# SignPath Foundation 서명 절차

이 문서는 Microsoft Partner Center 없이 Windows `.exe`에 정상적인
Authenticode 서명을 받기 위한 공개 오픈소스 배포 절차입니다. Defender나
SmartScreen을 우회하는 방법이 아닙니다.

## 이 저장소가 준비한 것

- MIT 공개 라이선스와 제3자 구성 요소 고지
- GitHub-hosted Windows 빌드 워크플로
- 태그에서만 실행되는 서명 요청 단계
- `AnimeAudioCaptioner.exe`만 Authenticode 서명 대상으로 지정한
  `.signpath/artifact-configurations/windows-x64.xml`

서명 요청 전에는 워크플로가 **미서명 검사용 산출물**만 만들며, 이를 일반
사용자에게 배포하면 안 됩니다.

## 유지자가 한 번만 하는 설정

1. 이 저장소 전체를 공개 GitHub 저장소로 올립니다. SignPath Foundation은
   모든 구성 요소가 OSI 승인 오픈소스 라이선스여야 하며, 공개된 저장소에서
   재현 가능한 빌드를 요구합니다.
2. SignPath Foundation에 프로젝트를 신청하고 저장소 URL을 프로젝트에
   등록합니다.
3. SignPath에서 GitHub.com을 Trusted Build System으로 연결하고, `main` 및
   `release/*`에서만 허용되는 `release-signing` 정책과 origin verification을
   설정합니다.
4. 이 저장소의
   `.signpath/artifact-configurations/windows-x64.xml`을 SignPath 프로젝트의
   artifact configuration으로 등록하고 slug를 정합니다.
5. GitHub 저장소 설정에 아래 값을 넣습니다. 비밀 값은 절대로 소스에 넣지
   않습니다.

| GitHub 위치 | 이름 | 값 |
| --- | --- | --- |
| Actions secret | `SIGNPATH_API_TOKEN` | SignPath CI 사용자 API 토큰 |
| Actions secret | `SIGNPATH_EXTENDED_VERIFICATION_TOKEN` | SignPath에서 발급한 GitHub origin verification 토큰 |
| Actions variable | `SIGNPATH_ORGANIZATION_ID` | SignPath 조직 ID |
| Actions variable | `SIGNPATH_PROJECT_SLUG` | 승인된 SignPath 프로젝트 slug |
| Actions variable | `SIGNPATH_SIGNING_POLICY_SLUG` | 보통 `release-signing` |
| Actions variable | `SIGNPATH_ARTIFACT_CONFIGURATION_SLUG` | `windows-x64` 또는 SignPath에서 설정한 slug |

## 서명된 공개본 만들기

1. `v0.1.0` 형식의 Git 태그를 `main`에 푸시합니다.
2. GitHub Actions의 **Build and sign Windows release**가 Windows-hosted runner에서
   self-contained `win-x64` 빌드를 만듭니다.
3. SignPath Foundation이 빌드 원본과 정책을 검증한 뒤 ZIP 안의
   `AnimeAudioCaptioner.exe`에 Authenticode 서명을 추가합니다.
4. 워크플로의 `anime-audio-captioner-win-x64-signed` 산출물만 배포합니다.
5. 깨끗한 Windows 11 PC에서 파일 속성의 **디지털 서명** 탭으로 서명이
   유효한지, Defender가 탐지하지 않는지, Chrome 탭 소리가 유지되는지,
   일본어 음성이 한국어 오버레이로 나오는지 확인합니다.

## Chrome 확장 배포

`chrome-extension/`은 Chrome Web Store용 소스입니다. Web Store에 게시한 뒤
Chrome에서 일반 설치한 확장 프로그램만 사용자용으로 안내합니다. 개발자
모드의 압축 해제 설치는 서명 전·게시 전 테스트에만 사용합니다.

## 알아둘 점

서명은 “알 수 없는 게시자” 문제를 정상적인 방식으로 줄이는 조건입니다.
새 서명자 또는 새 파일은 SmartScreen 평판이 쌓이는 동안 별도의 경고가 날 수
있으므로, 서명 완료와 깨끗한 Windows 테스트를 통과하기 전에는 공개본으로
배포하지 않습니다.
