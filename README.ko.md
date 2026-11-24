<div align="center">

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/assets/banner-dark.svg" />
  <source media="(prefers-color-scheme: light)" srcset="docs/assets/banner-light.svg" />
  <img alt="NeuralRig Studio" src="docs/assets/banner-dark.svg" width="100%" />
</picture>

<br />

[![Release](https://img.shields.io/badge/release-v3.0.0-18181b?style=flat-square&labelColor=09090b)](https://github.com/srain0626/NeuralRigStudio/releases)
[![npm](https://img.shields.io/badge/npm-neuralrig--mcp%20v3.0.0-18181b?style=flat-square&labelColor=09090b)](https://www.npmjs.com/package/neuralrig-mcp)
[![Platform](https://img.shields.io/badge/platform-Windows%20x64-18181b?style=flat-square&labelColor=09090b)](#빠른-시작)
[![License](https://img.shields.io/badge/license-MIT-52525b?style=flat-square&labelColor=09090b)](LICENSE)

[English](README.md) · **한국어 (Korean)** · [공식 웹](https://neuralrigstudio.seltum.xyz)

[빠른 시작](#빠른-시작) · [범용 오디오 · 프리셋](#범용-오디오-인터페이스--첫인상-프리셋) · [새 페달·앰프 제작 & npx MCP](#새-페달-앰프-캐비넷-추가-및-다중-ai-mcp-연동) · [벤치마크](#벤치마크-스위트) · [클라우드 · 허브 · OTA](#클라우드-동기화-기어-허브-버그-리포트-ota) · [소스 빌드](#소스-빌드)

</div>

---

**NeuralRig Studio**는 순수 네이티브 C#(`.NET 10`, `AVX2`/`FMA`) 기반 실시간 기타 멀티 이펙트, MNA SPICE 회로 시뮬레이터, Neural Amp Modeler(`.nam`) 워크스테이션입니다.

모든 DSP 연산(4x HIIR 폴리페이즈 오버샘플링, Newton-Raphson 반도체/다이오드 솔버, Norman Koren 12AX7 진공관 모델, KCL 패시브 톤스택, 16,384탭 Zero-Latency Partitioned FFT 컨볼버, `WaveNet`/`LSTM`/`ConvNet` SIMD 추론)이 오디오 콜백 스레드 **`0 B` 힙 할당** 제약 아래 네이티브로 실행됩니다.

> 메인테이너: **Ray Kim** (`raykim@users.noreply.github.com`) · AI 에이전트 문서 인덱스: [AGENTS.md](AGENTS.md) · [llms.txt](llms.txt) · [docs/INDEX.md](docs/INDEX.md)

---

## 빠른 시작

### 방법 1: Windows 인스톨러 (`Setup.exe`)
1. [GitHub Releases](https://github.com/srain0626/NeuralRigStudio/releases)에서 `NeuralRigStudio-v3.0.0-Setup-win-x64.exe`를 내려받아 실행합니다.
2. `%LOCALAPPDATA%\Programs\NeuralRigStudio` 경로에 `NeuralRigStudio.exe`와 `NeuralRigMcp.exe`가 함께 설치되며 시작 메뉴·바탕화면 바로가기 및 표준 제거 메뉴가 등록됩니다.

### 방법 2: 무설치 단일 실행 파일 (`.exe`)
1. `NeuralRigStudio-v3.0.0-win-x64-standalone.exe`를 내려받아 원하는 폴더에서 바로 실행합니다.
2. 별도 런타임이나 외부 폴더 없이 첫 실행 시 웹 UI 번들을 `%LOCALAPPDATA%\NeuralRigStudio\wwwroot_bundle`에 자동 준비합니다.

### 최초 실행 흐름
1. **Google 로그인 또는 게스트 모드 선택**: 첫 실행 시 Google 로그인(클라우드 설정 동기화·커뮤니티 기어 허브) 또는 **게스트 / 오프라인 모드** 중 하나를 선택합니다. 게스트 모드에서도 모든 오디오 DSP, 프리셋, AI 톤 아키텍트, SPICE 엔진은 제한 없이 동작합니다.
2. **인터랙티브 UI 튜토리얼 · 5종 섀시 테마**: 6단계 가이드가 오디오 인터페이스 연결, 8종 대표 프리셋, 2×8 시그널 체인, AI 톤/DI 프로파일러, 새 페달 생성, 벤치마크 실행 방법을 차례로 안내합니다. 모든 UI 창은 5종 하드웨어 섀시 마감 테마(`Putty`, `Carbon`, `Zine`, `Field`, `Cobalt`)와 한/영 전환을 동일하게 따르며 상단 **GUIDE** 버튼으로 언제든 다시 열 수 있습니다.
3. **기타 입력 연결**: 상단 **Audio I/O**에서 사용 중인 ASIO 인터페이스(Focusrite, Steinberg, Audient, Behringer, Roland, Line 6, Valeton 등) 또는 드라이버리스 WASAPI 라인 인풋(모노 `Ch 1..N` / 스테레오 페어)을 선택하세요.

---

## 범용 오디오 인터페이스 · 첫인상 프리셋

`AudioRouter`는 특정 하드웨어에 종속되지 않고 범용 오디오 인터페이스를 우선 자동 감지합니다:
- **ASIO 풀듀플렉스 직결 (`AsioDirectDuplex`)**: 단일 ASIO 인터페이스의 입출력을 직접 구동합니다.
- **ASIO 입력 → 락프리 링 버퍼 → WASAPI 출력 (`Wasapi`)**: 외장 오디오 인터페이스로 입력받고 PC 스피커/헤드폰으로 모니터할 때 MMCSS `"Pro Audio"` 우선순위로 `4ms / 8ms / 16ms / 32ms` 지연 프로파일을 선택할 수 있습니다.
- **드라이버리스 WASAPI 입력 (`WASAPI`)**: 전용 ASIO 드라이버 없이도 Windows APO(음성 노이즈 억제/AGC)를 우회해 순수 기타 DI 입력을 받고, `44.1kHz ↔ 48kHz` 무할당 리샘플 브리지로 출력 장치와 동기화합니다.

초기 실행 직후 바로 테스트할 수 있는 8개 쇼케이스 프리셋(`1A`–`2D`):

| 뱅크 | 프리셋명 | 시그널 체인 구성 |
|:---|:---|:---|
| `1A` | **Velvet Neo-Soul Clean** | SP Comp → Jan Ray → US Blackface Twin + 1x12 Jensen → DC-2W → Spring |
| `1B` | **Golden Edge of Breakup** | Orange Squeezer → TS808 + Klon → Brit 1Star → Alnico Blue → Plate |
| `1C` | **Brit 800 Arena Crunch** | Adaptive Gate → SD-1 Boost → JCM800 2203 → 1960BV V30 → Hall |
| `1D` | **Tokyo Anime Pop-Rock** | SP Comp → BD-2 → Parallel JC-120 + AC30 Split → DD-3T 점8분음표 → Hall |
| `2A` | **Tight Modern Djent** | Gate → TS808 Boost → 5150 III Red → Mesa 4x12 + GE-7 미드 컷 |
| `2B` | **Cathedral Shimmer Wash** | SP Comp → AC30 → Alnico Blue → CE-2 → TimeLine Tape → blueSky Shimmer |
| `2C` | **LA Hot-Rod Brown Lead** | Gate → EP Booster → BE-100 Brown Eye → 1960BV V30 → Analog BBD Delay |
| `2D` | **Psychedelic '68 Fuzz** | Cry Baby Wah → Ge AC128 Fuzz Face → Plexi 100 → Uni-Vibe → Spring |

검색창과 AI MCP에서는 저작권 안전 가칭(`Green Screamer 808` 등)뿐 아니라 원래 모델명(`TS808`, `SD-1`, `JCM800`)과 한글 명칭으로도 즉시 매칭됩니다.

---

## 새 페달, 앰프, 캐비넷 추가 및 다중 AI MCP 연동

기본 75종+ 카탈로그 외에 원하는 장비가 있다면 AI 에이전트를 통해 실제 회로도 기반으로 직접 추가할 수 있습니다.

1. 상단 **CRAFT** 버튼을 열면 단계별 제작 파이프라인, 복사 가능한 프롬프트, AI 클라이언트별 MCP 설정을 확인할 수 있습니다.
2. npm 공식 레지스트리에 배포된 [`neuralrig-mcp`](https://www.npmjs.com/package/neuralrig-mcp)를 아래 명령 한 줄로 Claude Desktop, Claude Code, Cursor, Windsurf, OpenAI Codex CLI, Gemini CLI, Antigravity에 자동 등록하세요:
   ```bash
   npx -y neuralrig-mcp install --client all
   ```
   설치 및 스튜디오 파이프 연결 점검:
   ```bash
   npx -y neuralrig-mcp doctor
   ```
3. **실제 회로도 기반 5단계 생성 파이프라인**:
   - AI가 `neuralrig_get_spice_guide` 스키마를 조회 → 웹에서 실제 회로도/반도체 데이터시트 리서치(자료가 없으면 추측하지 않고 없다고 답변) → 부족한 소자를 `neuralrig_register_spice_model`로 등록 → `neuralrig_simulate_spice`로 `.OP`/`.AC`/`.TRAN` 및 `0 B` RT 검증 통과 → `neuralrig_commit_spice_device`로 스튜디오에 즉시 핫로드.
4. **외부 `.nam` / `.wav` IR / `.nrig` 파일**: 스튜디오 창 위로 파일을 드래그 앤 드롭하면 즉시 로드됩니다.

상세 MCP 도구 명세는 [docs/MCP_INTEGRATION.md](docs/MCP_INTEGRATION.md)를 참고하세요.

---

## 벤치마크 스위트

상단 **BENCH** 버튼 또는 `RigBench.exe`로 내 PC 환경의 정량 지표를 즉시 측정할 수 있습니다:
- **하드웨어 유사도 검증 (스위트 평균 `98.2%` 실측 통과)**:
  - `Ibanez TS808` MNA SPICE `.AC` `720Hz` 미드 험프 (`100Hz` 대비 `+10.3dB`, `97.6%`)
  - `BOSS SD-1` 비대칭 3-다이오드 2차 배음(`H2=-23.6dBc`) vs 대칭 `TS808` (`-120.0dBc`, `99.2%`)
  - `Fuzz Face (AC128)` MNA 바이어스 및 배음 밀도 (`98.5%`)
  - `Fender '65 Twin Reverb (12AX7 + FMV 5-5-5)` `650Hz` 미드 스쿱 `-9.7dB` (`95.9%`)
  - `NAM WaveNet Feather` `64fr` vs `256fr` 블록 경계 일치율 (`ESR=0.00E+000`, `100.0%`)
- **지연시간 · RT 데드라인**: `64` / `128` / `256` 프레임(@ 48kHz) 기준 단일 페달 `0.0237ms`, 6-블록 리그 `0.0509ms`, 듀얼 앰프 병렬 리그 `0.0678ms`, MNA SPICE `0.1855ms`(`13.9% CPU`) 모두 `64fr` 버짓(`1.333ms`) 내 **`RT alloc: 0 B`** 통과.
- **16슬롯 멀티체인 스트레스**: 16슬롯 직렬 풀체인, 16슬롯 병렬 듀얼 앰프 매트릭스, 4단 하이게인 캐스케이드(`SoftKneeCeiling`), MNA SPICE(x2)+8192탭 FFT 캐비넷 IR 체인 모두 무할당·유한 출력 통과.

---

## 클라우드 동기화, 기어 허브, 버그 리포트, OTA

공개 라이브 백엔드는 `cloud/` (Vercel Next.js, 공식 주소 [neuralrigstudio.seltum.xyz](https://neuralrigstudio.seltum.xyz) + Supabase PostgreSQL/Google Auth)로 구성되며 데스크톱 앱과 연동됩니다:
- **별도 설정 없는 고정 엔드포인트**: 데스크톱 앱은 내부 고정 주소(`https://neuralrigstudio.seltum.xyz`, `CloudPlatformService.DefaultCloudBaseUrl`)에 자동 연결되며 UI에 서버 URL을 노출하거나 변경을 요구하지 않습니다.
- **오프라인 우선 설계**: 게스트 모드에서는 네트워크나 Supabase 설정 없이도 모든 로컬 DSP·프리셋·SPICE 제작·VST3/CLAP 내보내기 기능이 완전히 동작합니다.
- **Google 로그인 & 설정 동기화**: 루프백 OAuth로 Google 계정에 연결해 저장 프리셋, 기타 DI 프로필, 커스텀 SPICE 기어를 클라우드에 동기화합니다.
- **커뮤니티 기어 허브 (`HUB · VST3 → SPICE 기어 허브`)**: 사용자가 생성한 SPICE 페달·앰프·캐비넷을 업로드하고, 설치 전 로컬 `SpiceAnalyzer` 물리/무할당 검증을 거쳐 안전하게 받아 적용합니다.
- **자동 로그 버그 제보 (`BUG`)**: `LogSanitizer`가 OS 사용자명, 홈 경로, 토큰을 자동 마스킹한 최근 로그와 오디오 진단 정보를 첨부해 서버로 전송하며 오프라인일 경우 로컬 큐에 보관 후 재연결 시 자동 발송합니다.
- **선택 / 강제 OTA 업데이트**: 선택 업데이트는 사용자가 보류(`나중에`)할 수 있지만, `forceUpdate` 정책 또는 `minSupportedVersion` 미만 버전은 차단 모달이 표시되어 업데이트를 완료해야 계속 이용할 수 있습니다.
- **인앱 어드민 콘솔 (`ADMIN`)**: 관리자 Google 계정(`NEURALRIG_ADMIN_EMAILS` 또는 `profiles.is_admin`)으로 로그인하면 서비스 애널리틱스, 익명화 로그 기반 버그 트리아지, OTA 선택/강제 업데이트 정책 패널이 열립니다.

포크(Fork) 환경에서 자체 클라우드를 운영하려면 [cloud/.env.example](cloud/.env.example)을 참고해 `cloud/.env.local`과 Vercel 환경 변수를 설정하고, Supabase에 마이그레이션 3종([001_neuralrig_cloud.sql](cloud/supabase/migrations/001_neuralrig_cloud.sql), [002_rls_zero_service_role_and_rpc.sql](cloud/supabase/migrations/002_rls_zero_service_role_and_rpc.sql), [003_fix_app_releases_anon_policy.sql](cloud/supabase/migrations/003_fix_app_releases_anon_policy.sql) — Service Role 키 없는 사용자 JWT 전달형 RLS)을 순서대로 적용한 뒤 `CloudPlatformService.DefaultCloudBaseUrl`을 배포 주소로 변경하여 빌드하세요.

---

## 소스 빌드

필수 도구: **.NET 10 SDK** (`net10.0-windows` 포함), **Node.js 20+**.

```bash
# 1. React 웹 UI 빌드 (wwwroot/ 생성)
npm --prefix web-ui install
npm --prefix web-ui run build

# 2. 솔루션 Release 빌드 및 MCP 서버 퍼블리시
dotnet build NeuralRigStudio.sln -c Release
dotnet publish tools/NeuralRigMcp/NeuralRigMcp.csproj -c Release -r win-x64 --self-contained false -o bin/Release/net10.0-windows/win-x64/publish

# 3. 정량 DSP/SPICE/플랫폼 검증 실행 (RT alloc: 0 B 필수)
./tools/RigBench/bin/Release/net10.0-windows/RigBench.exe
```

릴리즈 배포 파일(`NeuralRigStudio-v3.0.0-Setup-win-x64.exe`, `NeuralRigStudio-v3.0.0-win-x64-standalone.exe`, `SHA256SUMS.txt`) 일괄 생성:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/build-release.ps1
```

## 기여 및 보안

- 기여 가이드: [CONTRIBUTING.md](CONTRIBUTING.md)
- 보안 취약점 제보 및 PII 처리 정책: [SECURITY.md](SECURITY.md)
- 라이선스: [MIT License](LICENSE) — Copyright (c) 2026 Ray Kim
