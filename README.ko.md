<div align="center">

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/assets/banner-dark.svg" />
  <source media="(prefers-color-scheme: light)" srcset="docs/assets/banner-light.svg" />
  <img alt="NeuralRig Studio" src="docs/assets/banner-dark.svg" width="100%" />
</picture>

<br />

[![Release](https://img.shields.io/badge/release-v3.2.0-18181b?style=flat-square&labelColor=09090b)](https://github.com/srain0626/NeuralRigStudio/releases)
[![Platform](https://img.shields.io/badge/platform-Windows%20x64-18181b?style=flat-square&labelColor=09090b)](#빠른-시작)
[![Plugins](https://img.shields.io/badge/plugins-VST3%20%7C%20CLAP%20v1.2-18181b?style=flat-square&labelColor=09090b)](#daw-플러그인-vst3--clap-v12)
[![License](https://img.shields.io/badge/license-MIT-52525b?style=flat-square&labelColor=09090b)](LICENSE)

[English](README.md) · **한국어 (Korean)** · [로드맵](docs/ROADMAP.md) · [공식 웹](https://neuralrigstudio.seltum.xyz)

[빠른 시작](#빠른-시작) · [범용 오디오 · 프리셋](#범용-오디오-인터페이스--첫인상-프리셋) · [DAW 플러그인](#daw-플러그인-vst3--clap-v12) · [커스텀 SPICE & MCP](#새-페달-앰프-캐비넷-추가-및-다중-ai-mcp-연동) · [벤치마크](#벤치마크-스위트) · [클라우드 · OTA](#클라우드-동기화-기어-허브-버그-리포트-ota) · [소스 빌드](#소스-빌드)

</div>

---

**NeuralRig Studio**는 네이티브 C#(`.NET 10`, `AVX2`/`FMA`), C++20, React 19 + WebView2로 작성된 실시간 기타 멀티 이펙트, MNA SPICE 회로 시뮬레이터, Neural Amp Modeler(`.nam`) 호스트 및 VST3/CLAP DAW 플러그인입니다.

모든 DSP 커널은 백그라운드 Python 런타임 없이 네이티브 관리/NativeAOT 오디오 스레드에서 실행되며, 4x HIIR 폴리페이즈 오버샘플링, Newton-Raphson 반도체/다이오드 솔버, Norman Koren 12AX7 진공관 모델, KCL 패시브 톤스택, 16,384탭 Zero-Latency Partitioned FFT 컨볼버, `Vector256<float>` 기반 `WaveNet`/`LSTM`/`ConvNet` 추론 전 구간에서 **콜백 블록당 `0 B` 힙 할당**을 보장합니다.

> 메인테이너: **Ray Kim** (`38514329+srain0626@users.noreply.github.com`) · 문서 인덱스: [AGENTS.md](AGENTS.md) · [docs/INDEX.md](docs/INDEX.md) · [docs/ROADMAP.md](docs/ROADMAP.md)

---

## 빠른 시작

### 방법 1: 통합 Windows 인스톨러 (`Setup.exe`)
1. [GitHub Releases](https://github.com/srain0626/NeuralRigStudio/releases)에서 `NeuralRigStudio-v3.2.0-Setup-win-x64.exe`를 내려받아 실행합니다.
2. 설치 옵션 창에서 데스크톱 앱과 DAW 플러그인을 함께 설치할 수 있습니다:
   - `%LOCALAPPDATA%\Programs\NeuralRigStudio`: `NeuralRigStudio.exe`, `NeuralRigMcp.exe`
   - `%LOCALAPPDATA%\Programs\Common\VST3\NeuralRigStudio.vst3` (선택, 기본 체크)
   - `%LOCALAPPDATA%\Programs\Common\CLAP\NeuralRigStudio` (선택, 기본 체크)
   - 시작 메뉴·바탕화면 바로가기, `HKCU\Software\NeuralRigStudio\Installer` 무인 OTA 설정 저장 및 표준 제거 지원

### 방법 2: 무설치 포터블 & 플러그인 단독 압축판
- **단일 실행 파일**: `NeuralRigStudio-v3.2.0-win-x64-standalone.exe`를 내려받아 바로 실행합니다(첫 실행 시 웹 UI 번들을 `%LOCALAPPDATA%\NeuralRigStudio\wwwroot_bundle`에 자동 준비).
- **DAW 플러그인 압축판**: `NeuralRigStudio-v3.2.0-VST3-win-x64.zip` 또는 `NeuralRigStudio-v3.2.0-CLAP-win-x64.zip`을 각각 시스템/사용자 VST3, CLAP 경로에 압축 해제해 사용할 수 있습니다.

### 최초 실행 흐름
1. **Google 로그인 또는 게스트 모드 선택**: Google 로그인(클라우드 설정 동기화·커뮤니티 기어 허브) 또는 **게스트 / 오프라인 모드**를 선택합니다. 게스트 모드에서도 로컬 DSP, 프리셋, AI 톤 아키텍트, SPICE 엔진은 제한 없이 동작합니다.
2. **가이드 · 5종 섀시 테마**: 6단계 가이드가 오디오 인터페이스 연결, 8종 대표 프리셋, 2×8 시그널 체인, AI 톤/DI 프로파일러, 새 페달 생성, 벤치마크 실행 순서를 안내합니다. 모든 UI는 5종 하드웨어 섀시 테마(`Putty`, `Carbon`, `Zine`, `Field`, `Cobalt`)와 한/영 전환을 따르며 상단 **GUIDE** 버튼으로 다시 열 수 있습니다.
3. **기타 연결 및 게인 스테이징**: 상단 **Audio I/O**에서 ASIO 드라이버 또는 WASAPI 장치(모노/스테레오 채널 매핑)를 고르고, 헤더 **IN/OUT** 게인 슬라이더 및 클립 홀드 미터로 헤드룸을 맞출 수 있습니다.

---

## 범용 오디오 인터페이스 · 첫인상 프리셋

`AudioRouter`는 특정 하드웨어에 종속되지 않는 4가지 범용 오디오 토폴로지를 지원합니다:
- **ASIO 풀듀플렉스 직결 (`AsioDirectDuplex`)**: 단일 ASIO 인터페이스의 입출력을 직접 구동합니다.
- **ASIO 입력 -> 락프리 링 버퍼 -> WASAPI 출력 (`Wasapi`)**: 외장 ASIO 인터페이스로 기타를 입력받고 PC 스피커/헤드폰으로 모니터할 때 MMCSS `"Pro Audio"` 우선순위로 `4ms / 8ms / 16ms / 32ms` 지연 프로파일을 선택합니다.
- **드라이버리스 WASAPI 입력 (`WASAPI`)**: 전용 ASIO 드라이버 없이도 Windows Raw APO(음성 노이즈 억제/AGC)를 우회하고 `44.1kHz <-> 48kHz` 무할당 리샘플 브리지로 출력 장치와 동기화합니다.
- **듀얼 ASIO (`AsioOut`)**: 입력과 출력 ASIO 장치를 분리해 사용합니다.

초기 실행 직후 테스트할 수 있는 8개 쇼케이스 프리셋(`1A`–`2D`):

| 뱅크 | 프리셋명 | 시그널 체인 구성 |
|:---|:---|:---|
| `1A` | **Velvet Neo-Soul Clean** | SP Comp -> Jan Ray -> US Blackface Twin + 1x12 Jensen -> DC-2W -> Spring |
| `1B` | **Golden Edge of Breakup** | Orange Squeezer -> TS808 + Klon -> Brit 1Star -> Alnico Blue -> Plate |
| `1C` | **Brit 800 Arena Crunch** | Adaptive Gate -> SD-1 Boost -> JCM800 2203 -> 1960BV V30 -> Hall |
| `1D` | **Tokyo Anime Pop-Rock** | SP Comp -> BD-2 -> Parallel JC-120 + AC30 Split -> DD-3T 점8분음표 -> Hall |
| `2A` | **Tight Modern Djent** | Gate -> TS808 Boost -> 5150 III Red -> Mesa 4x12 + GE-7 미드 컷 |
| `2B` | **Cathedral Shimmer Wash** | SP Comp -> AC30 -> Alnico Blue -> CE-2 -> TimeLine Tape -> blueSky Shimmer |
| `2C` | **LA Hot-Rod Brown Lead** | Gate -> EP Booster -> BE-100 Brown Eye -> 1960BV V30 -> Analog BBD Delay |
| `2D` | **Psychedelic '68 Fuzz** | Cry Baby Wah -> Ge AC128 Fuzz Face -> Plexi 100 -> Uni-Vibe -> Spring |

카탈로그 검색창과 AI MCP에서는 저작권 안전 가칭(`Green Screamer 808` 등)뿐 아니라 원래 모델명(`TS808`, `SD-1`, `JCM800`)과 한글 명칭으로도 매칭됩니다.

---

## DAW 플러그인 (VST3 & CLAP v1.2)

네이티브 C++20 셸이 Win32 자식 `HWND` + WebView2 UI를 호스팅하고 `NativeCore` AOT DLL(`IL2026`/`IL3050` 경고 0개)과 ABI v2로 통신합니다:
- **160개 오토메이션 파라미터**: 인/아웃풋 게인(`-24..+24 dB`), 드라이브/믹스, Scene A-H(`0..7`), 호스트 템포(`40..280 BPM`), 16슬롯 바이패스 및 노브 제어.
- **MIDI & 트랜스포트 동기화**: `CC#1`/`CC#4`/`CC#11`(Wah/Whammy 익스프레션 또는 Drive), `CC#7`(아웃풋 게인), `PC#`(씬 전환) 및 호스트 PPQ 재생 위치 기반 LFO/모듈레이션 위상 정렬(`?host=vst3` / `?host=clap`).
- **동적 PDC · Tail 보고**: `PitchCapo`/`PitchWhammy`/`PitchOctaver` 활성 시 `1024` 샘플 지연 보상(미사용 시 `0`)과 활성 딜레이/리버브 잔향 길이(Tail)를 DAW에 정확히 전달합니다.
- **상태 왕복 검증**: `Vst3HostVerify.exe` 및 `ClapHostVerify.exe`로 `.nrig` 상태 저장/복원, 실시간 오디오 처리, 실제 WebView2 리사이징을 검증합니다.

---

## 새 페달, 앰프, 캐비넷 추가 및 다중 AI MCP 연동

1. 상단 **CRAFT** 버튼에서 단계별 파이프라인, 프롬프트, AI 클라이언트별 MCP 설정을 확인할 수 있습니다.
2. `neuralrig-mcp`를 Claude Desktop, Claude Code, Cursor, Windsurf, OpenAI Codex CLI, Gemini CLI, Antigravity에 설치하세요:
   ```bash
   # GitHub Release 배포 .tgz 압축판 기준
   npm install -g ./neuralrig-mcp-3.2.0.tgz
   neuralrig-mcp install --client all

   # 스튜디오 Named Pipe 연결 점검
   neuralrig-mcp doctor
   ```
3. **실제 회로도 기반 5단계 생성 파이프라인**:
   - AI가 `neuralrig_get_spice_guide` 스키마 조회 -> 실제 회로도/반도체 데이터시트 조사(자료 없으면 추측 대신 중단) -> 부족한 소자를 `neuralrig_register_spice_model`로 등록 -> `neuralrig_simulate_spice`로 `.OP`/`.AC`/`.TRAN` 및 `0 B` RT 검증 -> `neuralrig_commit_spice_device`로 즉시 핫로드.
4. **외부 `.nam` / `.wav` IR / `.nrig` 파일**: 스튜디오 창에 파일을 드래그 앤 드롭하면 즉시 로드됩니다.

상세 MCP 도구 명세는 [docs/MCP_INTEGRATION.md](docs/MCP_INTEGRATION.md)를 참고하세요.

---

## 벤치마크 스위트

상단 **BENCH** 버튼 또는 `RigBench.exe`로 내 PC 환경의 정량 지표를 측정할 수 있습니다(`RigBenchmarkSuite.cs`):
- **하드웨어 유사도 검증 (스위트 평균 `98.2%`)**: TS808 `720Hz` 미드 험프(`97.6%`), SD-1 비대칭 H2 vs TS808(`99.2%`), Fuzz Face AC128 바이어스(`98.5%`), '65 Twin Reverb 12AX7 + FMV 톤스택(`95.9%`), NAM WaveNet 블록 경계 일치율(`100.0%`).
- **지연시간 · RT 데드라인 · v3.2 DAW 검증**: `64` / `128` / `256` 프레임(@ 48kHz) 기준 `64fr` 버짓(`1.333ms`) 내 처리 및 오디오 콜백, 파라미터 오토메이션, MIDI CC/PC, PPQ 동기화, PDC/Tail 계산 전 구간 **`0 B` RT 할당** 통과.
- **16슬롯 멀티체인 스트레스**: 16슬롯 직렬/병렬 듀얼 앰프 매트릭스, 4단 하이게인(`SoftKneeCeiling`), MNA SPICE(x2)+8192탭 FFT 캐비넷 IR 체인 검증.

---

## 클라우드 동기화, 기어 허브, 버그 리포트, OTA

선택형 클라우드 백엔드는 `cloud/` (Vercel Next.js, 공식 주소 [neuralrigstudio.seltum.xyz](https://neuralrigstudio.seltum.xyz) + Supabase PostgreSQL/Google Auth)로 구성됩니다:
- **오프라인 우선**: 게스트 모드에서 네트워크 없이도 로컬 DSP, 프리셋, SPICE 제작, VST3/CLAP 실행, `.nrig` 번들 및 벤치마크가 모두 동작합니다.
- **Google 로그인 & 기어 허브 (`HUB · VST3 -> GEAR HUB`)**: 루프백 OAuth(`http://127.0.0.1:<port>/callback`)로 설정/프로필을 동기화하고 커뮤니티 SPICE 기어를 `SpiceAnalyzer` 검증 후 1클릭 설치할 수 있습니다. (원격 기어 업로드 영구 저장에는 `cloud/`의 Supabase 설정이 필요합니다.)
- **익명화 버그 제보 (`BUG`) & OTA (`OtaUpdateService`)**: `LogSanitizer`로 사용자명, 홈 경로, 호스트명, 토큰을 제거해 전송하며 오프라인 큐(`%AppData%\NeuralRigStudio\bug_reports_outbox.json`)와 선택/강제 OTA를 지원합니다.
- **인앱 어드민 콘솔 (`ADMIN`)**: `NEURALRIG_ADMIN_EMAILS` / `profiles.is_admin` 계정에서 활성화됩니다.

포크 환경 자체 배포 시 [cloud/.env.example](cloud/.env.example)을 `cloud/.env.local`로 복사하고 [cloud/supabase/migrations](cloud/supabase/migrations)의 `001`~`003` 마이그레이션을 순서대로 적용한 뒤 Vercel(`Root Directory: cloud`)에 배포하고 `CloudPlatformService.DefaultCloudBaseUrl`을 갱신해 빌드하세요.

---

## 소스 빌드

필수 도구: **.NET 10 SDK** (`net10.0-windows` 포함), **Node.js 20+**, **Visual Studio 2022 C++ Build Tools**, **WebView2 NuGet 패키지**(`dotnet build`로 자동 복원).

```powershell
# 1. React 웹 UI 빌드 (wwwroot/ 생성)
npm --prefix web-ui install
npm --prefix web-ui run build

# 2. 솔루션 Release 빌드, MCP 퍼블리시, RigBench 정량 검증 (RT alloc: 0 B)
dotnet build NeuralRigStudio.sln -c Release
dotnet publish tools/NeuralRigMcp/NeuralRigMcp.csproj -c Release -r win-x64 --self-contained false -o bin/Release/net10.0-windows/win-x64/publish
.\tools\RigBench\bin\Release\net10.0-windows\RigBench.exe

# 3. MCP 검증 및 Next.js 클라우드 빌드 검증
node packages/neuralrig-mcp/test/verify-mcp.mjs
npm --prefix cloud ci
npm --prefix cloud run build

# 4. v3.2.0 릴리즈 아티팩트 일괄 생성 (Setup, Standalone, VST3.zip, CLAP.zip, MCP .tgz, SHA256SUMS.txt)
powershell -ExecutionPolicy Bypass -File scripts/build-release.ps1 -Version 3.2.0
# C++ 툴체인 없이 데스크톱/단일 실행 파일만 빌드하려면 -SkipPlugins 추가
```

## 기여 및 보안

- 기여 가이드: [CONTRIBUTING.md](CONTRIBUTING.md)
- 보안 취약점 제보 및 PII 처리 정책: [SECURITY.md](SECURITY.md)
- 라이선스: [MIT License](LICENSE), Copyright (c) 2026 Ray Kim
