<div align="center">

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/assets/banner-dark.svg" />
  <source media="(prefers-color-scheme: light)" srcset="docs/assets/banner-light.svg" />
  <img alt="NeuralRig Studio" src="docs/assets/banner-dark.svg" width="100%" />
</picture>

<br />

[![Platform](https://img.shields.io/badge/platform-Windows%20x64-18181b?style=flat-square&labelColor=09090b)](https://github.com/srain0626/NeuralRigStudio)
[![Runtime](https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square&labelColor=09090b)](https://dotnet.microsoft.com/)
[![SIMD](https://img.shields.io/badge/SIMD-AVX2%20%2F%20FMA-0ea5e9?style=flat-square&labelColor=09090b)](#real-time-benchmarks)
[![Audio I/O](https://img.shields.io/badge/audio-ASIO%20%C2%B7%20WASAPI-f97316?style=flat-square&labelColor=09090b)](#audio-io-midi--instrument-calibration)
[![RT Heap Alloc](https://img.shields.io/badge/RT%20alloc-0%20B%20%2F%20block-16a34a?style=flat-square&labelColor=09090b)](#architecture--threading-model)
[![MCP](https://img.shields.io/badge/MCP-stdio%20%2B%20Named%20Pipe-8b5cf6?style=flat-square&labelColor=09090b)](#model-context-protocol-mcp-server)
[![License](https://img.shields.io/badge/license-MIT-52525b?style=flat-square&labelColor=09090b)](LICENSE)

**English** · [한국어 (Korean)](README.ko.md)

[Architecture](#architecture--threading-model) · [DSP & Neural Engine](#dsp--neural-inference-internals) · [Benchmarks](#real-time-benchmarks) · [Device Catalog](#device-catalog-72-models) · [Audio, MIDI & Calibration](#audio-io-midi--instrument-calibration) · [MCP Server](#model-context-protocol-mcp-server) · [Build](#building-from-source)

</div>

---

**NeuralRig Studio** is a real-time guitar signal processor, white-box circuit simulator, and Neural Amp Modeler (`.nam`) workstation written in native C# (`.NET 10`, `System.Runtime.Intrinsics.X86`) with a WebView2 / React 19 console frontend.

The entire DSP core—4× polyphase half-band oversampling, damped Newton-Raphson diode/transistor solvers, multi-stage 12AX7 triode preamps, symbolic KCL passive tone stacks, SIMD cabinet convolution, and `Vector256<float>` neural network inference (`WaveNet`, `LSTM`, `ConvNet`)—executes directly inside the managed runtime without C++/ONNX native wrappers or background Python sidecars, while enforcing a strict **`0 B` heap allocation budget** on the real-time audio callback thread.

---

## Technical Overview

- **Zero-Allocation Real-Time Audio Path (`0 B / block`)**: All delay lines, polyphase filter histories, biquad states, convolution buffers, and neural workspaces are pre-allocated during `Prepare()`. UI edits, MIDI CC sweeps, and preset changes compile off-thread into immutable `CompiledSnapshot` structs and swap onto the audio thread via atomic `Volatile.Write` / `Volatile.Read` references—no `lock`, no GC pressure, and no zipper noise.
- **2×8 Parallel-Capable Matrix & 8 Click-Free Scenes (`A–H`)**: 16 processing blocks arranged across two stereo lanes supporting serial chaining (`Row 1 → Row 2`) and variable split/merge parallel topologies (`splitCol` / `mergeCol` with wet/dry crossfade), isolated dual-amp / dual-cab state pools, and up to **3 concurrent independent `.nam` captures** per preset.
- **Component-Level Analog Modeling (72 Models)**: Physical Shockley diode equations (`1N914 Si`, `1N34A Ge`, `Red LED`, asymmetric 3-diode feedback), `2N7000` MOSFET, `2SK30A / J201` JFET, `OC44 / 2N5088` BJT stages, `LM308` slew-rate limiting, 12AX7 cathode/grid-conduction dynamics, 4-node FMV passive tone stacks, and push-pull power amp `B+` rail sag with negative-feedback `Presence` / `Depth`.
- **Native `.nam` Runtime (`WaveNet` / `LSTM` / `ConvNet`)**: Pure C# AVX2/FMA neural runtime supporting official Neural Amp Modeler `Nano`, `Feather`, `Standard`, LSTM, and ConvNet architectures with bit-exact `.nam` round-trip serialization.
- **Cross-Clock Audio I/O, Hardware MIDI & Tempo Sync**: Supports ASIO Direct Full-Duplex, ASIO-to-WASAPI (Shared or Exclusive with MMCSS `"Pro Audio"` thread priority), and Dual-ASIO cross-device routing with zero-allocation `44.1 kHz ↔ 48 kHz` sinc sample-rate bridging. Includes WinMM MIDI CC/PC + interactive MIDI Learn, global BPM rhythmic subdivision sync (`1/4`, `1/8.`, `1/8`, `1/8T`, `1/16`), and a lock-free backing track loop player.
- **DI Instrument Calibration & Out-of-Process MCP**: 6-step Guided Guitar DI Profiler (trimmed-mean noise floor, `60 Hz` mains hum separation, 8192-point FFT harmonic comb), McLeod Pitch Method (`NSDF`) chromatic tuner (`±0.5 ct`), and a standalone stdio JSON-RPC 2.0 MCP server (`NeuralRigMcp`) communicating over Windows Named Pipes (`\\.\pipe\NeuralRigStudio_McpPipe_v1`).

---

## Architecture & Threading Model

Real-time audio callbacks fail deterministically if blocked by UI rendering, garbage collection, IPC serialization, or kernel mutexes. NeuralRig Studio isolates execution into three strict domains connected exclusively through lock-free single-producer/single-consumer (SPSC) power-of-2 ring buffers and atomic snapshot pointers:

```text
┌──────────────────────────────────────────────────────────────────────────────────────┐
│ 1. Out-of-Process & UI Surfaces                                                      │
│                                                                                      │
│   ┌─────────────────────────────────────┐    ┌────────────────────────────────────┐  │
│   │ WebView2 Frontend (React 19 + TS)   │    │ NeuralRigMcp.exe (Out-of-Process)  │  │
│   │ 2x8 Matrix · Scenes A-H · 50fps FFT │    │ Stdio JSON-RPC 2.0 <-> AI Client   │  │
│   └──────────────────┬──────────────────┘    └─────────────────┬──────────────────┘  │
└──────────────────────┼─────────────────────────────────────────┼─────────────────────┘
                       │ window.chrome.webview JSON              │ \\.\pipe\NeuralRigStudio_McpPipe_v1
                       ▼                                         ▼
┌──────────────────────────────────────────────────────────────────────────────────────┐
│ 2. Host Control Plane (WinForms STA + WinMM MIDI + Background I/O)                   │
│                                                                                      │
│   RigWebBridge <── MidiRouter (CC / PC / Learn / 140ms Debounce / 4-Tap BPM)         │
│        │                                                                             │
│        ├─ GuitarAnalyzer (6-Step DI Calibration, 8192-pt FFT Harmonic Comb)          │
│        ├─ AntigravityToneArchitect (Guitar-Aware 16-Slot + 8-Scene Synthesis)        │
│        └─ SignalChainEngine.UpdateChain() -> Compiles flat CompiledSnapshot + NAM WS │
└──────────────────────┬─────────────────────────────────────────▲─────────────────────┘
                       │ Volatile.Write(CompiledSnapshot)        │ Lock-Free Dry/Wet
                       ▼                                         │ SPSC RingBuffers (8192)
┌────────────────────────────────────────────────────────────────┴─────────────────────┐
│ 3. Real-Time Audio Callback (ASIO Driver Callback / WASAPI MMCSS "Pro Audio")        │
│    Invariants: 0 B Heap Allocation · No Locks · Pre-Allocated Scratch & State Pools  │
│                                                                                      │
│   ASIO In ─► [44.1k<->48k Sinc Bridge] ─► Dry Tap ─► ToneProcessor                   │
│                                                       ├─ 2x8 SignalChainEngine       │
│                                                       ├─ 4x HIIR + WhiteBox / Pedals │
│                                                       ├─ AVX2/FMA NamWorkspace[0..2] │
│                                                       └─ Stereo Cab IR Convolver     │
│                                                                │                     │
│   ASIO / WASAPI Out ◄── Wet Tap ◄── [Lock-Free MR Backing Mixer] ◄┘                  │
└──────────────────────────────────────────────────────────────────────────────────────┘
```

### Solution Layout

The DSP kernel is decoupled from Windows desktop dependencies (`WinForms` / `WebView2`) inside `src/NeuralRig.Core` (`net10.0`) so that the standalone benchmark runner (`RigBench`) and out-of-process MCP server (`NeuralRigMcp`) link against the exact same DSP implementation without pulling in UI assemblies:

```text
NeuralRigStudio/
├── NeuralRigStudio.sln
├── NeuralRigStudio.csproj              # Windows x64 Host (WinForms + WebView2 + ASIO/WASAPI)
├── MainForm.cs / Program.cs            # PerMonitorV2 DPI host window & WebView2 lifecycle
├── Audio/
│   └── AudioRouter.cs                  # ASIO duplex, Universal WASAPI capture, Dual-ASIO, MR mixer
├── Services/
│   ├── RigWebBridge.cs                 # JS IPC dispatcher & Named Pipe server (McpPipe_v1)
│   └── MidiRouter.cs                   # WinMM MIDI CC/PC router, MIDI Learn & Tap Tempo
├── src/
│   └── NeuralRig.Core/                 # UI-agnostic DSP & neural engine (net10.0)
│       ├── Audio/
│       │   ├── SignalChainEngine*.cs   # 2x8 modular matrix, split/merge, 72 block kernels
│       │   ├── SchematicPedalDsp.cs    # 4x HIIR + Shockley Newton-Raphson pedal circuits
│       │   ├── WhiteBoxAmpEngine.cs    # 12AX7 stages, symbolic KCL FMV stack, power amp sag
│       │   ├── NamRuntime.cs           # AVX2/FMA WaveNet, LSTM & ConvNet .nam runtime
│       │   ├── PartitionedConvolver.cs # Zero-latency partitioned FFT convolver (up to 16k taps)
│       │   ├── CabinetIrBank.cs        # Embedded measured IRs & dual virtual mic synthesizer
│       │   ├── SincResampler.cs        # Blackman-Harris windowed sinc resampler
│       │   ├── GuitarAnalyzer.cs       # 6-step DI profiler & pickup classifier
│       │   ├── GuitarPitchDetector.cs  # McLeod Pitch Method (NSDF) fundamental tracker
│       │   ├── VisualizerAnalyzer.cs   # Radix-2 FFT spectrum & telemetry snapshot builder
│       │   └── RingBuffer.cs           # Lock-free power-of-2 stereo SPSC ring buffer
│       └── Services/
│           └── AntigravityToneArchitect.cs
├── web-ui/                             # React 19 + TypeScript + Vite + Tailwind CSS v4
├── Assets/CabIRs/                      # 7 microphone-measured speaker cabinet WAV IRs
├── tools/
│   ├── NeuralRigMcp/                   # Standalone stdio MCP server & CLI inspector
│   └── RigBench/                       # 36-check DSP precision & 0-allocation test suite
└── docs/                               # Deep-dive architecture, profiler & MCP specs
```

---

## DSP & Neural Inference Internals

### 1. Two-Stage 4× HIIR Polyphase Oversampling

Non-linear waveshapers fold harmonics above Nyquist back into the audible band. Instead of high-latency linear-phase FIR filters, `WhiteBoxAmpEngine` and `SchematicPedalDsp` run a two-stage **Laurent de Soras HIIR** equiripple polyphase allpass half-band cascade (`double`-precision internal state) for near-zero group delay during live monitoring:

| Stage | Rate Transition | Topology | Passband Ripple | Stopband Rejection |
| :--- | :--- | :--- | :--- | :--- |
| **Stage 1** | `1× ↔ 2×` | 10-coefficient (`5 + 5` polyphase allpass) | `0.000000 dB` (`0..0.45 fs`) | `-133.09 dB` (`≥ 0.55 fs`) |
| **Stage 2** | `2× ↔ 4×` | 4-coefficient (`2 + 2` polyphase allpass) | `0.000000 dB` (`0..0.28 fs`) | `-106.60 dB` (`≥ 0.72 fs`) |

Measured end-to-end round-trip accuracy in `RigBench`: **`+0.004 dB`** passband error at `1 kHz` and **`79.5 dB`** aliasing suppression on hard-clipped `7.5 kHz` test tones.

### 2. Semiconductor Physics & Op-Amp Slew Limiting (`SchematicPedalDsp`)

Pedal drive blocks solve physical circuit equations at 4× oversampled rates rather than static `tanh(x)` approximations. Feedback and shunt diode clippers iterate a damped Newton-Raphson Shockley solver at room-temperature thermal voltage ($V_T = 25.85\text{ mV}$):

$$I_D(V_D) = I_S \left( e^{\frac{V_D}{\eta V_T}} - 1 \right)$$

| Semiconductor / Element | Physical Parameters | Representative Circuits |
| :--- | :--- | :--- |
| **1N914 / 1N4148 / 1S1588 Si** | $I_S = 2.52\text{ nA},\; \eta = 1.75$ | TS808, TS9, Pure Sky, DS-1, RAT / RAT 2, Distortion+ |
| **Asymmetric 3-Diode Si** | $2\times\text{Si}$ forward / $1\times\text{Si}$ reverse | BOSS SD-1 (`8.03%` 2nd harm), Vemuram Jan Ray (`5.52%` 2nd harm) |
| **1N34A Germanium** | $I_S = 2.0\text{ }\mu\text{A},\; \eta = 1.60$ | Klon Centaur (dual-gang clean/Ge blend), Fuzz Face |
| **Red Diffused LED** | $I_S = 1.0\text{ pA},\; \eta = 3.20$ | High-headroom British crunch stages (Guv'nor, Angry crunch) |
| **Discrete Transistors** | `2N7000` MOSFET · `2SK30A/J201` JFET · `OC44/2N5088` BJT | Fulltone OCD, BD-2, EP Booster, Box of Rock, Big Muff Pi, Rangemaster |
| **LM308 Op-Amp Slew Rate** | Slew-rate limiter + reverse passive RC filter | ProCo RAT (`11.3 dB` sweep at `3.5 kHz`) |

### 3. White-Box Vacuum Tube Amplifier (`WhiteBoxAmpEngine`)

1. **Cascaded 12AX7 Triode Preamp**: Models grid conduction, RC coupling high-pass shifts, dynamic cathode bias drift, Miller high-frequency rolloff, and the classic `39 kΩ` unbypassed cold-clipper stage (`JCM800 2203`, `SLO-100`, `5153`), yielding progressive harmonic saturation from Clean (`14.5%` THD) through Plexi (`49.5%` THD) to High-Gain (`90.2%` THD with `76.2%` even-order warmth).
2. **4-Node Passive FMV Tone Stack**: Derived symbolically via Kirchhoff's Current Law (KCL) and discretized via a 3rd-order `double`-precision bilinear transform, reproducing authentic interactive potentiometer loading (`-8.3 dB` Blackface mid scoop at `5-5-5`, `14.5 dB` Bass range, `22.6 dB` Treble range).
3. **Push-Pull Power Amp**: Simulates envelope-driven `B+` supply rail sag (`1.33 dB` dynamic compression under heavy transient load), output transformer core saturation, and negative-feedback (`NFB`) `Presence` and resonant `Depth` loops.

### 4. Native AVX2/FMA `.nam` Neural Runtime (`NamRuntime`)

`NamRuntime` parses and executes `.nam` model files directly in C# using explicit `Vector256<float>` AVX2 and FMA intrinsics:
- **Supported Architectures**: Dilated causal **WaveNet** (gated & standard activations, multi-array head bus, `Nano` / `Feather` / `Standard` / custom channel widths), multi-layer **LSTM** (fused `ifgo` gate matrix evaluation with persistent `h`/`c` state), and **ConvNet**.
- **Multi-NAM Isolation**: Up to **3 independent `NamWorkspace` instances** (`NamPoolIdx 0..2`) execute concurrently inside a single 2×8 preset, enabling parallel dual-rig `.nam` amp blending or `.nam` drive pedal → `.nam` amp head stacking with zero shared-state crosstalk.

### 5. Measured Cabinet IR Convolution (`CabinetIrBank`)

Includes **7 embedded real microphone-measured cabinet impulse responses** (Shure SM57, Shure SM7B, and Soundwoofer captures; phase-aligned and normalized to `1024` taps on `Prepare()`) plus an external `.wav` loader supporting arbitrary sample rates via Blackman-Harris windowed sinc resampling (`SincResampler`). `StereoCabConvolver` uses hardware SIMD dot products with an automatic mono fast-path (`|L - R| ≤ 1e-6` cuts convolution cost by 50%) while preserving true stereo width (`max|L - R| = 0.1436`) when stereo chorus/dimension blocks precede the cabinet.

---

## Real-Time Benchmarks

Measured via `tools/RigBench` (`Release` build, `.NET 10.0`, `AVX2=True`, `FMA=True`, `Vector256<float>=True`).
Block size: **`256` frames @ `44.1 kHz`** (real-time deadline budget = **`5.80 ms`**; `5.33 ms` @ `48 kHz`).

| Processing Workload (`256` frames) | Latency / Block | RT Budget Used | RT Heap Alloc |
| :--- | ---: | ---: | ---: |
| **Parallel Dual-Amp + Dual-Cab** (isolated state pools) | `0.181 ms` | `3.12%` | `0 B` |
| **NAM WaveNet Feather** (`12ch × 10` + `8ch × 8` gated) | `0.228 ms` | `3.93%` | `0 B` |
| **NAM WaveNet Standard** (`16ch × 10` + `8ch × 10` gated) | `0.253 ms` | `4.36%` | `0 B` |
| **NAM WaveNet Nano** (`12ch × 10` gated) | `0.371 ms` | `6.39%` | `0 B` |
| **Hybrid Rig** (`4×` White-Box Preamp + `NAM WaveNet Standard`) | `0.430 ms` | `7.41%` | `0 B` |
| **White-Box Full Rig** (`4× HIIR` + TS808 + 12AX7 HighGain + FMV + PowerAmp + 512-tap Cab) | `0.432 ms` | `7.44%` | `0 B` |
| **Full 2×8 Matrix Rig** (16-slot chain + parallel stereo modulation/delay/reverb) | `0.648 ms` | `11.16%` | `0 B` |

`RigBench` executes **36 automated assertions** on every run—verifying `GC.GetAllocatedBytesForCurrentThread() == 0` across all 72 catalog models, bit-exact `.nam` export/import round-trips (`max_abs_diff = 0.00e+00`, `ESR = -23.77 dB`), diode harmonic symmetry, tuner pitch accuracy down to `-52 dBFS` weak fundamentals, and dynamic BPM tempo-sync transitions (`128 → 142 BPM`).

---

## Device Catalog (72 Models)

Every block in the 2×8 matrix accepts any of the **72 DSP models** across 9 categories:

| Category | Count | Highlights |
| :--- | ---: | :--- |
| **Amplifiers (`amp`)** | 11 | Plexi 100, JCM800 2203, JCM2000 DSL, Friedman BE-100, SLO-100, 5153 Red, Dual Rectifier, US Twin, CA 1Star, VOX AC30, Roland JC-120 |
| **Neural Capture (`nam`)** | 1 | Official `.nam` Loader (WaveNet / LSTM / ConvNet, up to 3 concurrent slots per preset) |
| **Cabinets (`cab`)** | 8 | 7 SM57/SM7B Measured IRs (4×12 V30, 1960A G12T-75, 1960BV, Mesa OS 4×12, AC30 Alnico, JC-120 2×12, Fender 1×12 Jensen) + Custom `.wav` IR |
| **Drive / Fuzz / Boost (`drive`)** | 25 | TS808, TS9, SD-1, BD-2, Klon, Jan Ray, Pure Sky, BB Preamp, OCD, Bluesbreaker, Box of Rock, RAT / RAT 2, DS-1, MT-2, HM-2, Ecstasy Red, Guv'nor, Distortion+, Big Muff, Fuzz Face, Rangemaster OC44, EP / RC / Micro Amp |
| **Modulation (`mod`)** | 7 | BOSS CE-2, BOSS DC-2W Dimension C, BOSS BF-2 Flanger, EHX Small Clone, MXR Phase 90, Shin-ei Uni-Vibe (1968), Optical Tremolo |
| **Delay (`delay`)** | 5 | BOSS DM-2W Analog, BOSS DD-3T Digital, Analog BBD, Stereo Ping-Pong Digital, Strymon TimeLine |
| **Reverb (`reverb`)** | 5 | Spring Tank, Studio Plate, Ambient Hall, BOSS RV-6, Strymon blueSky (Shimmer / Plate / Room) |
| **EQ & Wah (`eq`)** | 4 | Dunlop Cry Baby GCB-95 Wah, BOSS GE-7 7-Band, 5-Band Graphic EQ, Studio Parametric EQ |
| **Dynamics (`dyn`)** | 6 | Adaptive Noise Gate, 1176 FET, MXR Dyna Comp, BOSS CS-3, Xotic SP, Dan Armstrong Orange Squeezer |

<details>
<summary><strong>Expand complete 72-device catalog &amp; <code>modelId</code> reference</strong></summary>

<br />

#### Amplifiers (`amp`) & Neural Capture (`nam`)
| `modelId` | Display Name | Circuit / Notes |
| :--- | :--- | :--- |
| `amp_plexi` | Brit Plexi 100 Bright | Marshall 1959 Super Lead 100W, bright cap toggle |
| `amp_2203` | Marshall JCM800 (2203) | Cascaded 12AX7 master-volume rock preamp + `39 kΩ` cold clipper |
| `amp_dsl` | Marshall JCM2000 DSL | Modern British ultra-gain voicing |
| `amp_be100` | Friedman BE-100 | Hot-rodded British Brown Eye preamp + tight low-end filtering |
| `amp_slo100` | SLO 100 Lead | Soldano Super Lead Overdrive 100W cascaded cold-clipper topology |
| `amp_5153` | 5153 Red High-Gain | Modern American multi-stage tight high-gain lead channel |
| `amp_recto` | Mesa/Boogie Dual Rectifier | Modern high-gain recto voicing with deep low-end resonance |
| `amp_twin` | US Clean Twin | Fender AB763 Blackface Twin Reverb clean headroom + mid-scoop FMV stack |
| `amp_1star` | CA 1Star Clean 100W | Boutique California 100W warm clean / fat edge-of-breakup |
| `amp_ac30` | VOX AC30 Top Boost | Class-A chime, cathode-biased EL84 sag & Top Boost cut interaction |
| `amp_jc120` | Roland JC-120 Jazz Chorus | Solid-state stereo clean amp with built-in analog BBD stereo chorus/vibrato |
| `nam_capture` | NAM Neural Capture | Loads `.nam` WaveNet, LSTM, or ConvNet profiles (up to 3 slots simultaneously) |

#### Speaker Cabinets (`cab`)
| `modelId` | Display Name | Capture Source & Microphone |
| :--- | :--- | :--- |
| `cab_v30` | 4x12 V30 Modern | Kalthallen 4×12 Celestion Vintage 30 — Shure SM57 |
| `cab_1960a` | Marshall 1960A (G12T-75) | Kalthallen 4×12 Celestion G12T-75 — Shure SM57 |
| `cab_1960bv` | Marshall 1960BV (4x12 V30) | Marshall 1960 4×12 Celestion V30 — Shure SM57 |
| `cab_mesa412` | Mesa/Boogie Rectifier 4x12 | Mesa Oversized 4×12 Celestion V30 — Shure SM57 |
| `cab_alnico` | 2x12 VOX Alnico Blue | VOX AC30 2×12 Open Back — Shure SM57 on-axis |
| `cab_jc120` | 2x12 JC-120 Open Back | Roland JC-120 2×12 Open Back — Shure SM7B |
| `cab_jensen112` | 1x12 Fender Jensen Open | Fender Deluxe Reverb 1×12 Open Back — Soundwoofer PD |
| `cab_custom` | Custom WAV IR | User `.wav` loader with automatic Blackman-Harris sinc resampling |

#### Overdrive, Distortion, Fuzz & Boost (`drive`)
| `modelId` | Display Name | Topology |
| :--- | :--- | :--- |
| `od_ts808` | Green 808 (TS808) | Symmetric `1N914` feedback clipper + `720 Hz` HPF + active tone |
| `od_ts9` | Ibanez TS9 Tube Screamer | Hotter output resistor voicing of classic symmetric feedback clipper |
| `od_sd1` | BOSS SD-1 Super OverDrive | Asymmetric 3-diode silicon feedback clipper (rich even-order 2nd harmonics) |
| `od_bd2` | BOSS BD-2 Blues Driver | Cascaded discrete JFET stages + dual-stage clipping |
| `od_klon` | Mythic Centaur (Klon) | Dual-gang clean mid feed-forward summed with `1N34A` Ge hard clipper |
| `od_janray` | Vemuram Jan Ray | Asymmetric silicon saturation + interactive Bass/Treble shelving |
| `od_puresky` | Caline Pure Sky | Transparent Timmy-style symmetric clipper with pre-drive Bass cut & post Treble cut |
| `od_bbpreamp` | Xotic BB Preamp | Op-amp feedback soft clipper + ±15 dB Baxandall active 2-band EQ |
| `od_ocd` | Fulltone OCD | `2N7000` MOSFET + Ge threshold stage with `HP/LP` voicing switch |
| `od_bluesbreaker` | Marshall Bluesbreaker MK1 | Low-gain soft-clipping op-amp feedback stage |
| `od_boxofrock` | ZVEX Box of Rock (JTM45) | Cascaded BS170 MOSFET stages emulating cranked JTM45 sag |
| `dist_rat` | Rodent Dist (RAT) | Vintage LM308 slew-rate limited op-amp + hard Si shunt + reverse filter |
| `dist_rat2` | ProCo RAT 2 | Modern tight RAT 2 voicing with `11.3 dB` reverse filter sweep |
| `dist_ds1` | BOSS DS-1 Distortion | Transistor booster into hard shunt diodes + scooped passive Voicing network |
| `dist_mt2` | BOSS MT-2 Metal Zone | Dual-gain gyrator distortion + sweepable parametric Mid (`200 Hz – 5 kHz`) |
| `dist_hm2` | BOSS HM-2 Heavy Metal | Swedish chainsaw dual-gyrator resonator (`85 Hz` + `1 kHz` / `1.3 kHz`) |
| `dist_ecstasy` | Bogner Ecstasy Red | Multi-stage discrete JFET high-gain amp-in-a-box |
| `dist_guvnor` | Marshall Guv'nor MK1 | Red LED hard clipping + 3-band passive FMV-style EQ |
| `dist_plus` | MXR Distortion+ | Classic op-amp gain stage into `1N34A` / Si shunt clippers |
| `fuzz_bigmuff` | Big Pi Fuzz (Muff) | 4-transistor `2N5088` dual-feedback fuzz + `35.7 dB` RC crossfade tone stack |
| `fuzz_face` | Dallas Fuzz Face (Ge/Si) | Asymmetric two-transistor shunt-series feedback fuzz (`45.2%` 2nd harmonic) |
| `boost_rangemaster` | Dallas Rangemaster (OC44) | `OC44` Germanium treble booster with high-pass input cap |
| `boost_ep` | Xotic EP Booster | Discrete `2SK30A` JFET Maestro EP-3 preamp emulation |
| `boost_rc` | Xotic RC Booster | Clean transparent boost + active Bass/Treble EQ |
| `boost_microamp` | MXR Micro Amp | Single-knob +26 dB op-amp clean preamp boost |

#### Modulation (`mod`), Delay (`delay`), Reverb (`reverb`), EQ (`eq`) & Dynamics (`dyn`)
| `modelId` | Category | Display Name | Tempo Sync |
| :--- | :--- | :--- | :---: |
| `mod_ce2` | `mod` | BOSS CE-2 Chorus | Yes |
| `mod_dc2w` | `mod` | BOSS DC-2W Dimension C (4-Button Mode Matrix) | — |
| `mod_bf2` | `mod` | BOSS BF-2 Flanger | Yes |
| `mod_smallclone` | `mod` | EHX Small Clone Chorus | Yes |
| `mod_phase90` | `mod` | Phase 90 Phaser (4-Stage Allpass) | Yes |
| `mod_univibe` | `mod` | Shin-ei Uni-Vibe (1968 Photocell Phase) | Yes |
| `mod_tremolo` | `mod` | Optical Tremolo | Yes |
| `dly_dm2w` | `delay` | BOSS DM-2W Analog BBD Delay | Yes |
| `dly_dd3t` | `delay` | BOSS DD-3T Digital Delay | Yes |
| `dly_analog` | `delay` | Analog BBD Delay | Yes |
| `dly_digital` | `delay` | Digital Stereo Ping-Pong Delay | Yes |
| `dly_timeline` | `delay` | Strymon TimeLine Multi-Tap / Mod Delay | Yes |
| `rev_spring` | `reverb` | Spring Reverb | — |
| `rev_plate` | `reverb` | Plate Studio Reverb | — |
| `rev_hall` | `reverb` | Hall Ambient Reverb | — |
| `rev_rv6` | `reverb` | BOSS RV-6 Reverb | — |
| `rev_bluesky` | `reverb` | Strymon blueSky (Shimmer / Mod / Plate) | — |
| `eq_crybaby` | `eq` | Dunlop Cry Baby GCB-95 Wah (`12.3 dB` resonant sweep + Mix) | — |
| `eq_ge7` | `eq` | BOSS GE-7 7-Band Graphic EQ | — |
| `eq_graphic5` | `eq` | Studio 5-Band Mesa-Voiced Graphic EQ | — |
| `eq_parametric` | `eq` | 3-Band Parametric EQ + HPF/LPF | — |
| `gate_adaptive` | `dyn` | Adaptive Hysteresis Noise Gate | — |
| `comp_1176` | `dyn` | 1176 FET Limiting Amplifier | — |
| `comp_dyna` | `dyn` | MXR Dyna Comp Ross/OTA Compressor | — |
| `comp_cs3` | `dyn` | BOSS CS-3 Compression Sustainer | — |
| `comp_sp` | `dyn` | Xotic SP Parallel Blend Compressor | — |
| `comp_orangesqueezer` | `dyn` | Dan Armstrong Orange Squeezer | — |

</details>

---

## Audio I/O, MIDI & Instrument Calibration

### Audio Routing Topologies & Cross-Clock Bridging

`AudioRouter` supports four hardware configurations—spanning multi-channel studio ASIO interfaces (`Ch 1..N`), 1-click **Valeton GP-100** routing, and driverless **Universal Windows WASAPI Capture** for USB guitar cables and class-compliant interfaces:

1. **ASIO Direct Full-Duplex (`AsioDirectDuplex`)**: Single-callback zero-copy path when input and output share the same ASIO driver, with selectable mono/stereo input channel offsets (`Ch 1..N`).
2. **ASIO Input → Lock-Free SPSC Ring → WASAPI (`Wasapi`)**: Routes dedicated ASIO guitar hardware (including 1-click **Valeton GP-100** auto-detection) out to standard Windows headphones/speakers under MMCSS `"Pro Audio"` priority (Shared or Exclusive mode; `Extreme 4 ms`, `Low 8 ms`, `Balanced 16 ms`, `Safe 32 ms`).
3. **Universal Windows WASAPI Input (`WasapiRecorder`)**: Plug-and-play capture for driverless USB guitar cables, class-compliant audio interfaces, and PC Line-In/Mic without ASIO drivers. Enables `AUDCLNT_STREAMOPTIONS_RAW` (*Raw Instrument Mode*) by default to bypass Windows APO noise suppression/AGC that otherwise clips guitar sustain, with zero-allocation `Float32` / `PCM16` / `PCM24` / `PCM32` decoding.
4. **Dual-ASIO Cross-Device (`AsioOut`)**: Bridges two independent ASIO drivers (or WASAPI input → ASIO output). Whenever input and output sample rates differ (e.g., `44.1 kHz` USB guitar hardware feeding a `48 kHz` desktop endpoint), a pre-allocated sinc sample-rate converter bridges the clock domains with `0 B` runtime allocation.

A built-in **Lock-Free Backing Track (MR) Player** mixes looped `.wav` / `.mp3` backing audio post-rig directly into the output stage, keeping the pre-FX dry analysis tap completely isolated.

### Hardware MIDI Control & Rhythmic Tempo Sync

`MidiRouter` listens on WinMM hardware MIDI ports for foot controllers and expression pedals:
- **Interactive MIDI Learn**: Bind incoming Control Change (`0xB0`) messages in one click to continuous knob sweeps (`param`), slot bypass (`bypass`, 140 ms edge-debounced), direct scene recall (`scene` `A–H`), `tunerMute`, or `tapTempo`.
- **Program Change (`0xC0`)**: Recalls saved presets directly by MIDI program index.
- **Global BPM & Subdivision Sync**: Global tempo (`40–280 BPM`, manual entry or 4-tap rolling-window Tap Tempo) locks any Delay or Modulation block to `FREE`, `1/4`, `1/8.` (dotted eighth), `1/8`, `1/8T` (triplet), or `1/16`, persisted independently per Scene (`A–H`).

### 6-Step Guided Guitar DI Profiler & MPM Chromatic Tuner

Electric guitar output levels vary by over `18 dB` between vintage single-coils (`-20 dBFS` with `60 Hz` hum) and active humbuckers (`-3 dBFS`).
- **Guided DI Profiler (`GuitarAnalyzer`)**: Walks through 6 measurements (`Silence → E2 → D3 → B3 → E4 → Open Chord Strum`). Computes a `135–2400 Hz` trimmed-mean thermal noise floor isolated from `60 / 120 / 180 Hz` mains hum, validates played notes within `±80 ct`, runs an `8192`-point zero-padded FFT harmonic comb, classifies pickup architecture (*Vintage Single-Coil*, *Modern Single-Coil / P90*, *Vintage Low-Output Humbucker*, *High-Output Modern Humbucker*), and auto-calibrates `InputGainDb` and `AdaptiveGate` thresholds.
- **MPM Chromatic Tuner (`GuitarPitchDetector`)**: Implements Philip McLeod's **Normalized Square Difference Function (NSDF)** over a `4096`-sample window with `40 Hz` high-pass filtering and sub-octave anti-jump logic—preventing 2nd-harmonic octave errors on neck-pickup low E strings down to `-52 dBFS`, with `±0.5 ct` precision, adjustable `A4` reference (`420–460 Hz`), and lock-free stage mute. See [docs/GUITAR_PROFILER_AND_TUNER.md](docs/GUITAR_PROFILER_AND_TUNER.md) for mathematical details.

---

## Model Context Protocol (MCP) Server

`tools/NeuralRigMcp` builds a standalone, single-purpose MCP server (`NeuralRigMcp.exe`) speaking JSON-RPC 2.0 over `stdio` (`Content-Length` framed or newline-delimited auto-negotiation) and forwarding live rig mutations to `NeuralRigStudio.exe` over Windows Named Pipe `\\.\pipe\NeuralRigStudio_McpPipe_v1`.

### MCP Client Configuration

Register `NeuralRigMcp` in your MCP client config (e.g., `~/.gemini/antigravity/mcp_servers.json` or Claude Desktop / Cursor MCP config):

```json
{
  "mcpServers": {
    "neuralrig": {
      "command": "C:\\path\\to\\NeuralRigStudio\\NeuralRigMcp.exe",
      "args": []
    }
  }
}
```

When developing from source without publishing a binary:

```json
{
  "mcpServers": {
    "neuralrig": {
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "C:\\path\\to\\NeuralRigStudio\\tools\\NeuralRigMcp\\NeuralRigMcp.csproj",
        "-c",
        "Release"
      ]
    }
  }
}
```

### Exposed MCP Tools

| Tool | Purpose | Parameters |
| :--- | :--- | :--- |
| `neuralrig_get_catalog` | Inspect all 72 devices and parameter schemas (`min`, `max`, `step`, `unit`) | `category?` |
| `neuralrig_get_state` | Read live 16-slot chain, active scene (`A–H`), routing topology, and BPM | — |
| `neuralrig_get_guitar_profiles` | Read saved DI calibration profiles (pickup class, hum, suggested trim/gate) | — |
| `neuralrig_generate_tone` | Synthesize a calibrated 16-slot + 8-scene rig from a natural-language brief | `prompt`, `guitarProfileId?`, `autoSave?` |
| `neuralrig_set_preset` | Atomically load a complete 16-slot preset structure into the live engine | `presetName`, `slots`, `tempoBpm?`, `autoSave?` |
| `neuralrig_update_slot` | Patch a single slot's model, bypass state, rhythmic division, or parameters | `slotIndex` (`0..15`), `modelId?`, `enabled?`, `syncDivision?` (`0..5`), `params?` |
| `neuralrig_switch_scene` | Switch scene (`0..7`), serial/parallel routing (`0..1`), mix, or global BPM | `sceneIndex?`, `routingMode?`, `parallelMix?`, `tempoBpm?` |

For quick terminal testing without an AI client, `NeuralRigMcp` includes a one-shot CLI mode:

```powershell
NeuralRigMcp.exe --call neuralrig_get_state "{}"
NeuralRigMcp.exe --call neuralrig_switch_scene "{\"sceneIndex\":2,\"tempoBpm\":128}"
```

See [docs/MCP_INTEGRATION.md](docs/MCP_INTEGRATION.md) for protocol payloads and workflows.

---

## Building from Source

### Prerequisites

- **OS**: Windows 10 / 11 x64 (WebView2 Evergreen Runtime — preinstalled on Windows 11)
- **CPU**: x86-64 processor with `AVX2` and `FMA` instruction sets (Intel Haswell / AMD Excavator or newer recommended)
- **Toolchains**: [.NET 10 SDK](https://dotnet.microsoft.com/download) and [Node.js 20+](https://nodejs.org/)

### Build, Verify & Run

```powershell
# 1. Clone the repository
git clone https://github.com/srain0626/NeuralRigStudio.git
cd NeuralRigStudio

# 2. Build the React 19 frontend (outputs static assets into wwwroot/)
Push-Location web-ui
npm ci
npm run build
Pop-Location

# 3. Run the 36-check DSP accuracy & zero-allocation benchmark suite
dotnet run --project tools/RigBench/RigBench.csproj -c Release

# 4. Launch NeuralRig Studio
dotnet run --project NeuralRigStudio.csproj -c Release
```

### Publishing Standalone Release Binaries

```powershell
dotnet publish NeuralRigStudio.csproj -c Release -r win-x64 --self-contained false
dotnet publish tools/NeuralRigMcp/NeuralRigMcp.csproj -c Release -r win-x64 --self-contained false
```

To install a prebuilt release instead, download `NeuralRigStudio-v*-win-x64.zip` from [Releases](https://github.com/srain0626/NeuralRigStudio/releases), extract it to any folder, and run `NeuralRigStudio.exe`.

---

## Technical Documentation

- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) — Threading boundaries, lock-free memory pools, and white-box circuit topology
- [docs/GUITAR_PROFILER_AND_TUNER.md](docs/GUITAR_PROFILER_AND_TUNER.md) — 6-step DI calibration math, FFT harmonic comb, and MPM NSDF pitch tracking
- [docs/MCP_INTEGRATION.md](docs/MCP_INTEGRATION.md) — Named Pipe IPC framing and MCP tool schemas

---

## Acknowledgments & Credits

- **Oversampling Topology**: Polyphase half-band allpass IIR filter design after Laurent de Soras (*HIIR*).
- **Neural Amp Modeler Format**: `.nam` model specification by Steven Atkinson ([NeuralAmpModelerCore](https://github.com/sdatkinson/NeuralAmpModelerCore)).
- **Pitch Detection**: Normalized Square Difference Function (NSDF) after Philip McLeod & Geoff Wyvill (*A Smarter Way to Find Pitch*, ICMC 2005).
- **Measured Cabinet Impulse Responses**: Real Shure SM57 / SM7B and Soundwoofer acoustic captures from Kalthallen Cabs, Soundwoofer (Public Domain), [`orodamaral/Speaker-Cabinets-IRs`](https://github.com/orodamaral/Speaker-Cabinets-IRs), and [`jfsantos/guitar-fx`](https://github.com/jfsantos/guitar-fx). Full per-file provenance is documented in [Assets/CabIRs/CREDITS.txt](Assets/CabIRs/CREDITS.txt).

## License

Released under the [MIT License](LICENSE).
