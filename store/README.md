# 공개 오픈소스 정식 서명 배포 체크리스트

이 프로젝트는 Microsoft Partner Center 계정 없이 SignPath Foundation의 공개 오픈소스 Authenticode 서명 경로를 사용합니다. 전체 과정은 루트의 [SIGNPATH.md](../SIGNPATH.md)에 있습니다.

## 공개 전 확인

1. GitHub 저장소와 모든 소스가 공개되어 있는지 확인합니다.
2. `LICENSE`와 `THIRD-PARTY-NOTICES.md`가 포함되어 있는지 확인합니다.
3. 태그 빌드의 서명된 `AnimeAudioCaptioner.exe`만 사용자용 압축 파일에 포함합니다.
4. 깨끗한 Windows 11 환경에서 Defender 검사, 서명 정보, 탭 소리 유지, 일본어 음성 → 한국어 오버레이를 확인합니다.
5. Chrome 확장은 Chrome Web Store에 별도로 게시합니다.

## 금지하는 배포 방식

- Defender 예외 추가 요청
- SmartScreen 우회 안내
- 난독화·임시 패커 사용
- 화면 캡처·OCR 기반 실행 파일 재배포
- PowerShell을 자동 실행하는 설치 스크립트
