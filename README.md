# NeuralRig Studio 🎸⚡

[English](#english) | [한국어](#한국어)

---

<div id="english"></div>

## English

**NeuralRig Studio** is a pure native C# (.NET 10 + WebView2 + AVX2/FMA) real-time guitar multi-effects, amp modeling, and neural rig studio featuring an out-of-process **Antigravity AI MCP adapter**.

Inspired by hardware flagships like Neural DSP Quad Cortex, it combines schematic-level analog circuit modeling (4× HIIR oversampling, 12AX7 tube preamps, passive FMV tone stacks, dynamic power-amp sag) with native `.nam` (Neural Amp Modeler) WaveNet neural inference and measured cabinet impulse responses—all within a zero-allocation (`0 B` RT heap allocation) real-time audio thread.

[![Platform](https://img.shields.io/badge/Platform-Windows%2010%2F11%20x64-blue.svg)](https://github.com/srain0626/NeuralRigStudio)
[![Runtime](https://img.shields.io/badge/.NET-10.0%20Windows-512BD4.svg)](https://dotnet.microsoft.com/)
[![Audio](https://img.shields.io/badge/Audio-ASIO%20%7C%20WASAPI-orange.svg)](https://github.com/naudio/NAudio)
[![Real-Time Allocation](https://img.shields.io/badge/RT%20Alloc-0%20Bytes-brightgreen.svg)](#zero-allocation-dsp)
[![MCP](https://img.shields.io/badge/AI%20MCP-Google%20Antigravity-purple.svg)](docs/MCP_INTEGRATION.md)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

---

### ✨ Key Features

- **🎛️ Quad Cortex-Inspired Matrix UI**: 2x8 grid (16 slots total) supporting serial routing, split-parallel processing, and dual stereo paths. Includes 8 instant, click-free scene changes (A–H) per preset.
- **⚡ 72 Schematic-Modeled Devices**:
  - **Pedals**: TS808, SD-1, BD-2, Klon Centaur, OCD, Big Muff Pi, ProCo RAT 2, King of Tone, Jan Ray, Cry Baby Wah, CE-2 Chorus, Phase 90, FL-9 Flanger, Digital/Tape Delays, Plate/Hall/Shimmer Reverbs, and more.
  - **Amps**: Fender Twin Reverb (AB763), Marshall 1959 Super Lead (Plexi), Soldano SLO-100 High-Gain, Roland JC-120 Clean, Vox AC30 Top Boost, Bogner Ecstasy, Peavey 5150, and Mesa Dual Rectifier.
  - **Cabinet IRs**: 512/1024-tap real-mic measured impulse responses (Mesa 4x12, Marshall 1960A, Marshall 1960BV, Fender Jensen 1x12, Roland JC-120 2x12, Celestion Alnico, and Celestion V30) + custom `.wav` IR loader.
- **🧠 Native WaveNet NAM Runtime**: Embedded zero-allocation AVX2/FMA neural inference supporting `Nano`, `Feather`, and `Standard` `.nam` models with direct native import/export.
- **🎸 Guided Guitar DI Profiler**: 6-step guided wizard (Silence → E2 → D3 → B3 → E4 → Strum) that measures noise floor, pickup hum (60/50 Hz rejection), harmonic richness, auto-classifies pickup type (Single-Coil vs. Humbucker), and sets optimal input trim and noise gate thresholds.
- **🎯 Studio MPM Chromatic Tuner**: Precision chromatic tuner utilizing the McLeod Pitch Method (NSDF) with 4096-sample window, sub-octave anti-jump filtering, 21-LED display, cents deviation readout, A4 calibration (420–460 Hz), and lock-free real-time tuner mute.
- **🤖 Antigravity AI MCP Integration**: Standalone Model Context Protocol (MCP) server over Named Pipe IPC (`neuralrig-studio-pipe`). Talk to Antigravity AI to describe any desired tone, recreate artist setups, or optimize presets specifically for your guitar profile.
- **🔌 Valeton GP-100 Plug-and-Play**: Built-in 44.1 kHz ↔ 48 kHz zero-allocation sinc sample rate bridge for seamless plug-and-play with Valeton GP-100 USB audio and standard ASIO/WASAPI devices.

---

### 🚀 Quickstart & Download

1. Download the latest `NeuralRigStudio-v1.0.0-win-x64.zip` from [Releases](https://github.com/srain0626/NeuralRigStudio/releases).
2. Extract the archive anywhere on your Windows PC.
3. Launch `NeuralRigStudio.exe`.
4. Connect your guitar via an ASIO audio interface or USB multi-effects unit (e.g. Valeton GP-100).
5. Open the **Guitar Profiler** from the top bar to calibrate your instrument in 6 quick steps!

---

### 🤖 Antigravity MCP Setup

Connect NeuralRig Studio to Google Antigravity by adding the server to your Antigravity MCP configuration:

```json
{
  "mcpServers": {
    "neuralrig": {
      "command": "C:\\path\\to\\NeuralRigMcp.exe",
      "args": []
    }
  }
}
```

Now you can prompt Antigravity:
> *"Create a sparkly neo-soul rhythm guitar tone with subtle chorus and tape delay for my single-coil guitar."*

See [docs/MCP_INTEGRATION.md](docs/MCP_INTEGRATION.md) for full details.

---

### 🛠️ Building from Source

**Prerequisites**:
- Windows 10/11 x64
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Node.js 20+](https://nodejs.org/)

```powershell
# 1. Clone repository
git clone https://github.com/srain0626/NeuralRigStudio.git
cd NeuralRigStudio

# 2. Build React UI (outputs to wwwroot/)
cd web-ui
npm install
npm run build
cd ..

# 3. Build & Run Desktop App
dotnet run -c Release

# 4. Run Benchmark Suite (verifies 0-alloc & DSP precision)
dotnet run --project tools/RigBench/RigBench.csproj -c Release
```

---

<br/>
<hr/>
<br/>

<div id="한국어"></div>

## 한국어 (Korean)

**NeuralRig Studio**는 순수 네이티브 C# (.NET 10 + WebView2 + AVX2/FMA) 기반의 초저지연 실시간 기타 멀티이펙터/앰프 시뮬레이터 및 **Google Antigravity AI MCP 연동** 스튜디오입니다.

Neural DSP Quad Cortex 하드웨어 콘솔에서 영감을 얻은 직관적인 터치 그리드 UI와 함께, 회로 해석 기반의 아날로그 모델링(4배 오버샘플링 HIIR, 12AX7 진공관 프리앰프, FMV 톤스택, 파워앰프 새그 현상)과 네이티브 `.nam` WaveNet 딥러닝 추론 엔진, 실제 마이킹 측정 캐비닛 IR을 탑재하고 있습니다. 실시간 오디오 스레드는 **0 Bytes 힙 할당(`RT alloc: 0 B`)** 원칙을 엄격히 준수하여 끊김 없는 완벽한 실시간 연주 환경을 제공합니다.

---

### ✨ 핵심 기능

- **🎛️ Quad Cortex 스타일 2x8 매트릭스 그리드**: 2열 8슬롯(총 16슬롯)으로 직렬, 분기 병렬, 듀얼 스테레오 라우팅을 지원하며, 프리셋당 8개의 씬(Scene A–H)을 통해 팝 노이즈 없이 즉각적인 파라미터/바이패스 전환이 가능합니다.
- **⚡ 72종 아날로그 회로 모델링 디바이스**:
  - **드라이브/페달**: TS808, SD-1, BD-2, Klon Centaur, OCD, Big Muff Pi, ProCo RAT 2, King of Tone, Jan Ray, Cry Baby 와우, CE-2 코러스, Phase 90, FL-9 플랜저, 디지털/테이프 딜레이, 플레이트/홀/쉬머 리버브 등 72종.
  - **앰프 헤드**: Fender Twin Reverb (AB763), Marshall 1959 Super Lead (Plexi), Soldano SLO-100 High-Gain, Roland JC-120 Clean, Vox AC30 Top Boost, Bogner Ecstasy, Peavey 5150, Mesa Dual Rectifier.
  - **캐비닛 IR**: 실측 마이크 기반 512/1024-Tap 캐비닛 임펄스 응답(Mesa 4x12, Marshall 1960A, Marshall 1960BV, Fender Jensen 1x12, Roland JC-120 2x12, Celestion Alnico, V30) 내장 및 외부 `.wav` IR 로더 지원.
- **🧠 네이티브 WaveNet NAM 런타임**: C# AVX2/FMA 가속 기반으로 외부 라이브러리 의존성 없이 `Nano`, `Feather`, `Standard` `.nam` 모델을 실시간 0-할당으로 구동.
- **🎸 내 기타 DI 분석 & 프로파일러**: 6단계 가이드 마법사(무음 → E2 → D3 → B3 → E4 → 스트럼)를 통해 배경 잡음, 픽업 노이즈(60Hz 험 분리), 배음 밀도(Harmonics)를 측정하고 픽업 종류(싱글코일 vs 험버커)를 자동 분류하여 최적의 인풋 트림과 노이즈 게이트를 설정합니다.
- **🎯 스튜디오 MPM 크로매틱 튜너**: McLeod Pitch Method(NSDF) 기반 4096 샘플 분석 엔진으로 저음현 옥타브 점프를 완벽 방지하고, ±0.5센트 정밀도의 21-LED 인디케이터, A4 레퍼런스 조절(420~460Hz), 실시간 무음 튜너 뮤트(Mute)를 지원합니다.
- **🤖 Antigravity AI MCP 연동**: 네임드 파이프(`neuralrig-studio-pipe`) 기반 고속 IPC로 동작하는 단독 실행형 MCP 어댑터(`NeuralRigMcp.exe`)를 통해, Antigravity AI에게 원하는 사운드를 말하면 현재 기타 특성에 맞춰 최적의 16슬롯 톤을 자동으로 디자인하고 즉시 로드해줍니다.
- **🔌 Valeton GP-100 플러그앤플레이**: 44.1kHz ↔ 48kHz 실시간 0-할당 Sinc 리샘플링 브릿지가 내장되어 있어 Valeton GP-100 및 범용 오디오 인터페이스를 연결하자마자 설정 없이 바로 사용할 수 있습니다.

---

### 🚀 다운로드 및 실행 방법

1. [Releases](https://github.com/srain0626/NeuralRigStudio/releases) 페이지에서 최신 `NeuralRigStudio-v1.0.0-win-x64.zip`을 다운로드합니다.
2. 원하는 폴더에 압축을 풉니다.
3. `NeuralRigStudio.exe`를 실행합니다.
4. 오디오 인터페이스(또는 Valeton GP-100)를 연결하고 기타를 꽂습니다.
5. 상단 **'내 기타 DI 분석 & 프로파일러'** 마법사를 실행하여 6단계 음을 따라 치면 내 기타에 최적화된 톤 세팅이 완료됩니다!

---

### 🤖 Antigravity AI 연동 설정

Antigravity 설정 파일(예: `~/.gemini/antigravity/mcp_servers.json`)에 아래와 같이 `NeuralRigMcp.exe` 경로를 등록하세요:

```json
{
  "mcpServers": {
    "neuralrig": {
      "command": "C:\\path\\to\\NeuralRigMcp.exe",
      "args": []
    }
  }
}
```

이제 Antigravity 대화창에서 원하는 톤을 자유롭게 요청할 수 있습니다:
> *"내 싱글코일 기타 프로필을 확인하고, 존 메이어 스타일의 투명하고 다이내믹한 클린 크런치 톤을 만들어줘."*
> *"지금 프리셋에서 디스토션 게인을 살짝 낮추고 공간계 리버브 믹스를 25%로 올려줘."*

자세한 내용은 [docs/MCP_INTEGRATION.md](docs/MCP_INTEGRATION.md)를 참고하세요.

---

### 🛠️ 소스코드 빌드 방법

**요구 사양**:
- Windows 10/11 x64
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Node.js 20+](https://nodejs.org/)

```powershell
# 1. 저장소 복제
git clone https://github.com/srain0626/NeuralRigStudio.git
cd NeuralRigStudio

# 2. React UI 빌드 (wwwroot 폴더로 자동 번들링)
cd web-ui
npm install
npm run build
cd ..

# 3. 데스크톱 앱 빌드 및 실행
dotnet run -c Release

# 4. 실시간 오디오 벤치마크 및 0-할당 검증 (35개 전 항목 테스트)
dotnet run --project tools/RigBench/RigBench.csproj -c Release
```

---

## 📚 Technical Documentation

- 📐 [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) - Real-time zero-allocation engine & 72-device circuit DSP architecture
- 🤖 [docs/MCP_INTEGRATION.md](docs/MCP_INTEGRATION.md) - Model Context Protocol adapter setup and tool schema
- 🎸 [docs/GUITAR_PROFILER_AND_TUNER.md](docs/GUITAR_PROFILER_AND_TUNER.md) - Guided DI profiler algorithm, MPM tuner, and calibration details

---

## 📄 License

This project is licensed under the [MIT License](LICENSE).
