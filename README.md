<div align="center">

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/assets/banner-dark.svg" />
  <source media="(prefers-color-scheme: light)" srcset="docs/assets/banner-light.svg" />
  <img alt="NeuralRig Studio" src="docs/assets/banner-dark.svg" width="100%" />
</picture>

<br />

[![Release](https://img.shields.io/badge/release-v3.2.0-18181b?style=flat-square&labelColor=09090b)](https://github.com/srain0626/NeuralRigStudio/releases)
[![Platform](https://img.shields.io/badge/platform-Windows%20x64-18181b?style=flat-square&labelColor=09090b)](#quick-start)
[![Plugins](https://img.shields.io/badge/plugins-VST3%20%7C%20CLAP%20v1.2-18181b?style=flat-square&labelColor=09090b)](#daw-plugins-vst3--clap-v12)
[![License](https://img.shields.io/badge/license-MIT-52525b?style=flat-square&labelColor=09090b)](LICENSE)

**English** · [한국어 (Korean)](README.ko.md) · [Roadmap](docs/ROADMAP.md) · [Website](https://neuralrigstudio.seltum.xyz)

[Quick Start](#quick-start) · [Audio & Presets](#universal-audio--first-run-showcase-presets) · [DAW Plugins](#daw-plugins-vst3--clap-v12) · [Custom SPICE & MCP](#creating-custom-pedals-amps--cabinets) · [Benchmark Suite](#benchmark-suite) · [Cloud & OTA](#cloud-sync-gear-hub--ota) · [Build](#building-from-source)

</div>

---

**NeuralRig Studio** is a real-time guitar signal processor, physical MNA SPICE circuit simulator, Neural Amp Modeler (`.nam`) host, and native VST3/CLAP DAW plugin written in C# (`.NET 10`, `AVX2`/`FMA`), C++20, and React 19 + WebView2.

All DSP kernels run inside the native managed or NativeAOT audio thread with no background Python runtime, enforcing **`0 B` heap allocation per audio callback block** across 4x HIIR polyphase oversampling, Newton-Raphson transistor/diode solvers, Koren 12AX7 triode preamps, symbolic KCL tone stacks, 16,384-tap Zero-Latency Partitioned FFT cabinet convolution, and `Vector256<float>` neural inference (`WaveNet`, `LSTM`, `ConvNet`).

> Maintainer: **Ray Kim** (`38514329+srain0626@users.noreply.github.com`) · Docs: [AGENTS.md](AGENTS.md) · [docs/INDEX.md](docs/INDEX.md) · [docs/ROADMAP.md](docs/ROADMAP.md)

---

## Quick Start

### Option 1: Unified Windows Installer (`Setup.exe`)
1. Download `NeuralRigStudio-v3.2.0-Setup-win-x64.exe` from [GitHub Releases](https://github.com/srain0626/NeuralRigStudio/releases).
2. Run the installer and select optional DAW plugin targets. It installs:
   - `NeuralRigStudio.exe` and `NeuralRigMcp.exe` under `%LOCALAPPDATA%\Programs\NeuralRigStudio`
   - Optional VST3 bundle to `%LOCALAPPDATA%\Programs\Common\VST3\NeuralRigStudio.vst3`
   - Optional CLAP bundle to `%LOCALAPPDATA%\Programs\Common\CLAP\NeuralRigStudio`
   - Start Menu / Desktop shortcuts, remembered silent OTA options (`HKCU\Software\NeuralRigStudio\Installer`), and Add/Remove Programs uninstall support

### Option 2: Portable Standalone & Plugin Zips
- **Standalone EXE**: Download `NeuralRigStudio-v3.2.0-win-x64-standalone.exe` and run directly (`wwwroot_bundle` unpacks under `%LOCALAPPDATA%\NeuralRigStudio` on first launch).
- **DAW Plugin Zips**: Extract `NeuralRigStudio-v3.2.0-VST3-win-x64.zip` into your VST3 folder or `NeuralRigStudio-v3.2.0-CLAP-win-x64.zip` into your CLAP folder.

### First-Run Flow
1. **Account or Guest Mode**: Sign in with Google (Cloud Settings Sync and Community Gear Hub) or click **Continue as Guest (Offline Mode)** to play offline without an account.
2. **Guided Setup & Themes**: A 6-step walkthrough covers Audio I/O patching, 8 showcase presets (`1A`–`2D`), the 2x8 signal chain, AI Tone Architect, custom SPICE creation, and `RigBench`. All modals support 5 themes (`Putty`, `Carbon`, `Zine`, `Field`, `Cobalt`) and KO/EN switching (click **GUIDE** to replay).
3. **Connect Your Guitar**: Open **Audio I/O** to pick your ASIO driver or WASAPI device (`Mono` or `Stereo` pair), then balance headroom with the header **IN/OUT** gain controls and peak clip-hold meters.

---

## Universal Audio & First-Run Showcase Presets

`AudioRouter` handles four driver topologies with no vendor lock-in:
- **ASIO Full-Duplex Direct (`AsioDirectDuplex`)**: Direct callback execution on one hardware ASIO interface.
- **ASIO Input -> Lock-Free SPSC Ring -> WASAPI Output (`Wasapi`)**: Routes an external ASIO instrument interface to PC speakers/headphones using MMCSS `"Pro Audio"` priority (`4 ms` / `8 ms` / `16 ms` / `32 ms` ring buffers).
- **Driverless WASAPI Capture (`WASAPI`)**: Captures line-in audio with Raw APO bypass and `44.1 kHz <-> 48 kHz` zero-allocation windowed-sinc resampling.
- **Dual-ASIO (`AsioOut`)**: Independent ASIO input and output devices.

Eight showcase presets are preloaded in banks `1A`–`2D`:

| Bank | Preset | Topology Summary |
|:---|:---|:---|
| `1A` | **Velvet Neo-Soul Clean** | SP Comp -> Jan Ray -> US Blackface Twin + 1x12 Jensen -> DC-2W -> Spring Reverb |
| `1B` | **Golden Edge of Breakup** | Orange Squeezer -> TS808 + Klon -> Brit 1Star -> Alnico Blue -> Plate Reverb |
| `1C` | **Brit 800 Arena Crunch** | Adaptive Gate -> SD-1 Boost -> JCM800 2203 -> 1960BV V30 -> Hall Reverb |
| `1D` | **Tokyo Anime Pop-Rock** | SP Comp -> BD-2 -> Parallel JC-120 + AC30 Split -> DD-3T Dotted 8th -> Hall |
| `2A` | **Tight Modern Djent** | Gate -> TS808 Boost -> 5150 III Red -> Mesa 4x12 + GE-7 Mid Cut |
| `2B` | **Cathedral Shimmer Wash** | SP Comp -> AC30 -> Alnico Blue -> CE-2 -> TimeLine Tape Delay -> blueSky Shimmer |
| `2C` | **LA Hot-Rod Brown Lead** | Gate -> EP Booster -> BE-100 Brown Eye -> 1960BV V30 -> Analog BBD Delay |
| `2D` | **Psychedelic '68 Fuzz** | Cry Baby Wah -> Ge AC128 Fuzz Face -> Plexi 100 -> Uni-Vibe -> Spring Tank |

Catalog search matches both safe aliases (`Green Screamer 808`, `Brit 800 Lead`) and hardware model names (`Ibanez TS808`, `Marshall JCM800`).

---

## DAW Plugins (VST3 & CLAP v1.2)

Both formats embed the React 19 UI inside a native Win32 child `HWND` + WebView2 host calling the AOT-compiled `NeuralRig.NativeCore.dll` (zero `IL2026`/`IL3050` warnings):
- **160 Automatable Parameters**: Input/output gain (`-24..+24 dB`), drive/mix, Scene A-H (`0..7`), host tempo sync (`40..280 BPM`), and 16-slot bypass + knobs.
- **MIDI & Transport Sync**: Real-time MIDI `CC#1`/`CC#4`/`CC#11` (Wah/Whammy expression or Drive), `CC#7` (Output Gain), Program Change (`PC#` scenes), and sample-accurate host PPQ transport phase alignment (`?host=vst3` / `?host=clap`).
- **Dynamic PDC & Tail Reporting**: Reports `1024` samples latency when `PitchCapo`/`PitchWhammy`/`PitchOctaver` is active (`0` otherwise) and computed delay/reverb tail length to the DAW.
- **State Round-Trip**: Full `.nrig` chain + parameter state persistence verified by `Vst3HostVerify.exe` and `ClapHostVerify.exe`.

---

## Creating Custom Pedals, Amps & Cabinets

1. **Click `CRAFT` in the top bar** to open setup snippets and circuit prompts.
2. **Install `neuralrig-mcp`** across AI clients (Claude Desktop, Claude Code, Cursor, Windsurf, OpenAI Codex CLI, Gemini CLI, Antigravity):
   ```bash
   # From the GitHub Release tarball (recommended offline/local package)
   npm install -g ./neuralrig-mcp-3.2.0.tgz
   neuralrig-mcp install --client all

   # Verify Studio named pipe connectivity
   neuralrig-mcp doctor
   ```
3. **5-Step Schematic Pipeline**:
   - Inspects schema via `neuralrig_get_spice_guide`, researches authentic schematics (reporting honestly if none exists), registers `.MODEL` specs via `neuralrig_register_spice_model`, validates `.OP`/`.AC`/`.TRAN` + `0 B` RT allocation via `neuralrig_simulate_spice`, and hot-loads via `neuralrig_commit_spice_device`.
4. **Drag & Drop Assets**: Drop `.wav` cabinet IRs (up to 16,384 taps), `.nam` captures, or `.nrig` bundles onto the window.

See [docs/MCP_INTEGRATION.md](docs/MCP_INTEGRATION.md) for full tool schemas.

---

## Benchmark Suite

Run **BENCH** in the header (or `RigBench.exe`) to verify quantitative hardware similarity, latency, and RT safety (`RigBenchmarkSuite.cs`):
- **Hardware Similarity (`98.2%` suite average)**: TS808 `.AC` 720 Hz mid-hump (`97.6%`), SD-1 asymmetric H2 vs TS808 (`99.2%`), AC128 Fuzz Face MNA bias (`98.5%`), '65 Twin Reverb 12AX7 + FMV tone stack (`95.9%`), NAM WaveNet boundary invariance (`100.0%`).
- **Real-Time Budget & v3.2 Plugin Gate**: `64`/`128`/`256`-frame blocks @ 48 kHz under `1.333 ms` budget with `0 B` RT allocation across audio callbacks, parameter automation, MIDI CC/PC, PPQ transport sync, and PDC/tail reporting.
- **16-Slot Stress**: 16-slot Serial, 16-slot Parallel Dual-Amp Matrix, 4-stage `SoftKneeCeiling`, and MNA SPICE (x2) + 8,192-tap Partitioned FFT Cab IR.

---

## Cloud Sync, Gear Hub & OTA

Optional cloud features connect to Vercel Next.js (`cloud/`, hosted at [neuralrigstudio.seltum.xyz](https://neuralrigstudio.seltum.xyz)) backed by Supabase PostgreSQL + Google Auth:
- **Offline First**: DSP, presets, AI Tone Architect, SPICE creation, VST3/CLAP, `.nrig` bundles, and benchmarks run offline in Guest mode with zero network requirement.
- **Cloud Settings Sync & Community Gear Hub**: Google loopback OAuth (`http://127.0.0.1:<port>/callback`) unlocks cloud sync and 1-click Community Gear Hub installs (pre-screened with `SpiceAnalyzer`). Note: Community Gear Hub uploads require `Supabase` credentials in `cloud/` to persist remotely.
- **Scrubbed Bug Reports (`BUG`) & OTA (`OtaUpdateService`)**: Strips local usernames, file paths, hostnames, and tokens via `LogSanitizer`, queues offline reports in `%AppData%\NeuralRigStudio\bug_reports_outbox.json`, and supports optional or mandatory OTA updates.
- **In-App Admin Console (`ADMIN`)**: Enabled for accounts in `NEURALRIG_ADMIN_EMAILS` / `profiles.is_admin`.

To self-host `cloud/`, copy [cloud/.env.example](cloud/.env.example) to `cloud/.env.local`, apply migrations `001`–`003` under [cloud/supabase/migrations](cloud/supabase/migrations), deploy to Vercel (`Root Directory: cloud`), and point `CloudPlatformService.DefaultCloudBaseUrl` to your domain before building.

---

## Building from Source

Requirements: **.NET 10 SDK** (`net10.0-windows` desktop workload), **Node.js 20+**, **Visual Studio 2022 C++ Build Tools**, and **WebView2 NuGet package** (restored by `dotnet build` for VST3/CLAP native shells).

```powershell
# 1. Build React UI into wwwroot/
npm --prefix web-ui install
npm --prefix web-ui run build

# 2. Build .NET solution, publish MCP binary, and run RigBench gate (RT alloc: 0 B)
dotnet build NeuralRigStudio.sln -c Release
dotnet publish tools/NeuralRigMcp/NeuralRigMcp.csproj -c Release -r win-x64 --self-contained false -o bin/Release/net10.0-windows/win-x64/publish
.\tools\RigBench\bin\Release\net10.0-windows\RigBench.exe

# 3. Verify MCP package & Cloud Next.js build
node packages/neuralrig-mcp/test/verify-mcp.mjs
npm --prefix cloud ci
npm --prefix cloud run build

# 4. Build & verify all v3.2.0 release artifacts (Setup, Standalone, VST3.zip, CLAP.zip, MCP .tgz, SHA256SUMS.txt)
powershell -ExecutionPolicy Bypass -File scripts/build-release.ps1 -Version 3.2.0
# Pass -SkipPlugins to skip C++20 VST3/CLAP builds when only testing Desktop/Standalone
```

## Contributing & Security

- Contribution guide: [CONTRIBUTING.md](CONTRIBUTING.md)
- Vulnerability reporting & PII policy: [SECURITY.md](SECURITY.md)
- License: [MIT License](LICENSE), Copyright (c) 2026 Ray Kim
