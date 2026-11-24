# 01 — DSP, WaveNet, MNA SPICE & Benchmark Architecture

## Threading & Memory Contract
- Audio callback executes strictly with **0 B managed allocation** (`GC.GetAllocatedBytesForCurrentThread() == 0`).
- Pre-allocation happens in `ToneProcessor.Prepare(maxFrames, sampleRate)` and `SignalChainEngine.Prepare`.
- Parameter or slot graph edits compile off-thread and swap atomically into `_activeSnapshot` via `Volatile.Write`.
- Output limiting uses `ToneProcessor.SoftKneeCeiling` above `0.92f` + 3.5 Hz DC blocker.

## Key Subsystems
1. **WhiteBox Analog & Pedal DSP (`SchematicPedalDsp.cs`, `WhiteBoxAmpEngine.cs`)**:
   - 4x Laurent de Soras HIIR polyphase all-pass half-band oversampling.
   - Shockley Newton-Raphson diode clippers: symmetric (`1N914`, `TS808`) vs asymmetric 3-diode (`SD-1`, `Jan Ray`, `OCD`).
   - 12AX7 multi-stage triode preamps, symbolic KCL passive FMV tone stack, B+ power-amp sag/presence/depth.
2. **Real-Time MNA SPICE (`Spice/RealtimeSpiceCircuit.cs`, `Spice/SpiceAnalyzer.cs`)**:
   - Up to `MaxNodes = 36` per stage, Schur complement linear elimination + damped Newton-Raphson for `D, Q, J, M, U, T`.
   - `SpiceAnalyzer.AnalyzeCircuit(dto, knobs, sr, fTest, amp)` executes `.OP` DC bias, `.AC` sweep, `.TRAN` 4x harmonics, and a 64-block `0 B` RT allocation audit.
3. **Neural & Convolver (`ToneProcessor.cs`, `PartitionedConvolver.cs`)**:
   - Native C# AVX2/FMA `.nam` runtime (`WaveNet`, `LSTM`, `ConvNet`), up to 3 active instances per chain.
   - Zero-latency uniformly partitioned overlap-save FFT cab IR (`PartitionedIr.MaxTaps = 16384`, 256-sample direct head + 512-pt FFT tail).
4. **Benchmark Suite (`RigBenchmarkSuite.cs`)**:
   - Returns `RigBenchmarkReportDto` (Hardware Similarity average `98.2%`, 64/128/256fr CPU/allocs, 4x 16-slot stress tests).
   - Never mutates `%AppData%` settings.
